#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
use std::{collections::HashMap, io::{BufRead, BufReader, Write}, path::PathBuf, process::{Child, ChildStdin, Command, Stdio}, sync::{Arc, Mutex, atomic::{AtomicU64, Ordering}}, time::{Duration, Instant}};
use tauri::{Emitter, Manager, menu::{Menu, MenuItem}, tray::{TrayIconBuilder, TrayIconEvent}};
use tauri_plugin_dialog::DialogExt;
use tauri_plugin_opener::OpenerExt;
use serde_json::{json, Value};
use tokio::sync::oneshot;

struct Service {
    input: Mutex<Option<ChildStdin>>,
    child: Mutex<Option<Child>>,
    pending: Mutex<HashMap<u64, oneshot::Sender<Value>>>,
    sequence: AtomicU64,
}
impl Service {
    fn start(app: &tauri::AppHandle) -> Result<Arc<Self>, Box<dyn std::error::Error>> {
        let folder = if cfg!(debug_assertions) {
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../backend")
        } else { app.path().resource_dir()?.join("monitor") };
        let executable = folder.join(if cfg!(target_os = "windows") { "Divoom.HardwareMonitor.exe" } else { "Divoom.MacMonitor" });
        let mut command = Command::new(executable);
        command.arg("--bridge").current_dir(&folder).stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null());
        #[cfg(windows)] { use std::os::windows::process::CommandExt; command.creation_flags(0x08000000); }
        let mut child = command.spawn()?;
        let input = child.stdin.take();
        let output = child.stdout.take().ok_or("Hardware service stdout missing")?;
        let service = Arc::new(Self { input: Mutex::new(input), child: Mutex::new(Some(child)), pending: Mutex::new(HashMap::new()), sequence: AtomicU64::new(1) });
        let reader_service = service.clone();
        let handle = app.clone();
        std::thread::spawn(move || {
            for line in BufReader::new(output).lines().map_while(Result::ok) {
                if line.len() > 4_000_000 { continue; }
                if let Ok(message) = serde_json::from_str::<Value>(&line) {
                    if let Some(id) = message["id"].as_u64() {
                        if let Some(reply) = reader_service.pending.lock().unwrap().remove(&id) { let _ = reply.send(message); }
                    } else if message["type"].is_string() { let _ = handle.emit("monitor", message); }
                }
            }
            reader_service.input.lock().unwrap().take();
            for (_, reply) in reader_service.pending.lock().unwrap().drain() {
                let _ = reply.send(json!({"ok": false, "error": "Hardware service exited. Restart the application."}));
            }
            let _ = handle.emit("service-exited", ());
        });
        Ok(service)
    }
    async fn request(&self, command: String, payload: Value) -> Result<Value, String> {
        // The frontend can call only the fixed bridge protocol, never a shell or process path.
        if !["init","snapshot","discover","lcdMode","start","stop","save","preferences","rescan","diagnostics"].contains(&command.as_str()) { return Err("Unknown command".into()); }
        let id = self.sequence.fetch_add(1, Ordering::Relaxed);
        let (tx, rx) = oneshot::channel();
        self.pending.lock().unwrap().insert(id, tx);
        let bytes = format!("{}\n", json!({"id": id, "command": command, "payload": payload}));
        let write_result = {
            let mut input = self.input.lock().unwrap();
            match input.as_mut() {
                Some(stream) => stream.write_all(bytes.as_bytes()).and_then(|_| stream.flush()).map_err(|e| e.to_string()),
                None => Err("Hardware service unavailable".into())
            }
        };
        if let Err(error) = write_result { self.pending.lock().unwrap().remove(&id); return Err(error); }
        let response = tokio::time::timeout(Duration::from_secs(35), rx).await;
        self.pending.lock().unwrap().remove(&id);
        let value = response.map_err(|_| "Hardware service request timed out".to_string())?.map_err(|_| "Hardware service disconnected".to_string())?;
        if value["ok"].as_bool() != Some(true) { return Err(value["error"].as_str().unwrap_or("Hardware service error").to_owned()); }
        Ok(value["data"].clone())
    }
    fn stop(&self) {
        if let Some(mut input) = self.input.lock().unwrap().take() { let _ = writeln!(input, "{}", json!({"id": 0, "command": "quit"})); }
        if let Some(mut child) = self.child.lock().unwrap().take() {
            let deadline = Instant::now() + Duration::from_secs(5);
            while Instant::now() < deadline {
                if child.try_wait().ok().flatten().is_some() { return; }
                std::thread::sleep(Duration::from_millis(25));
            }
            let _ = child.kill(); let _ = child.wait();
        }
    }
}
struct Backend { service: Option<Arc<Service>>, error: String }
struct TrayLabels { show: MenuItem<tauri::Wry>, quit: MenuItem<tauri::Wry> }
fn show(app: &tauri::AppHandle) { if let Some(window) = app.get_webview_window("main") { let _ = window.show(); let _ = window.unminimize(); let _ = window.set_focus(); } }

#[tauri::command]
async fn bridge_request(state: tauri::State<'_, Backend>, command: String, payload: Option<Value>) -> Result<Value, String> {
    state.service.as_ref().ok_or_else(|| state.error.clone())?.request(command, payload.unwrap_or(Value::Null)).await
}
#[tauri::command]
fn set_language(app: tauri::AppHandle, labels: tauri::State<'_, TrayLabels>, code: String) -> Result<(), String> {
    let catalog: HashMap<String, String> = serde_json::from_str(include_str!("../../../locales/catalog.json")).map_err(|e| e.to_string())?;
    let keys: Vec<&str> = catalog["keys"].split('|').collect();
    let row: Vec<&str> = catalog.get(&code).unwrap_or(&catalog["en"]).split('|').collect();
    let text = |key: &str| -> String { keys.iter().position(|k| *k == key).and_then(|i| row.get(i)).unwrap_or(&key).to_string() };
    labels.show.set_text(text("显示监控面板")).map_err(|e| e.to_string())?;
    labels.quit.set_text(text("退出")).map_err(|e| e.to_string())?;
    if let Some(window) = app.get_webview_window("main") { let _ = window.set_title(&format!("Divoom / {} v{}", text("硬件监控"), app.package_info().version)); }
    Ok(())
}
#[tauri::command]
fn open_resource(app: tauri::AppHandle, resource: String) -> Result<(), String> {
    match resource.as_str() {
        "pawnio" => app.opener().open_url("https://pawnio.eu/", None::<&str>).map_err(|e| e.to_string()),
        "smartmontools" => app.opener().open_url("https://github.com/smartmontools/smartmontools/releases", None::<&str>).map_err(|e| e.to_string()),
        "logs" => {
            let path = if cfg!(target_os = "macos") { app.path().home_dir().map_err(|e| e.to_string())?.join("Library/Application Support/Divoom/HardwareMonitor") }
                else { app.path().local_data_dir().map_err(|e| e.to_string())?.join("Divoom/HardwareMonitor") };
            std::fs::create_dir_all(&path).map_err(|e| e.to_string())?;
            app.opener().open_path(path.to_string_lossy().to_string(), None::<&str>).map_err(|e| e.to_string())
        }
        _ => Err("Unknown resource".into())
    }
}
#[tauri::command]
async fn export_diagnostics(app: tauri::AppHandle, state: tauri::State<'_, Backend>) -> Result<bool, String> {
    let mut report = state.service.as_ref().ok_or_else(|| state.error.clone())?.request("diagnostics".into(), Value::Null).await?;
    report["DesktopVersion"] = json!(app.package_info().version.to_string());
    let path = app.dialog().file().set_file_name("Divoom-diagnostics.json").add_filter("JSON", &["json"]).blocking_save_file();
    if let Some(path) = path {
        std::fs::write(path.into_path().map_err(|e| e.to_string())?, serde_json::to_vec_pretty(&report).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
        return Ok(true);
    }
    Ok(false)
}
#[tauri::command]
async fn choose_smartctl(app: tauri::AppHandle) -> Result<Option<String>, String> {
    Ok(app.dialog().file().blocking_pick_file().map(|p| p.to_string()))
}
#[tauri::command]
fn restart_as_admin(app: tauri::AppHandle) -> Result<(), String> {
    #[cfg(windows)] {
        use std::os::windows::ffi::OsStrExt;
        #[link(name = "shell32")] extern "system" { fn ShellExecuteW(hwnd: isize, op: *const u16, file: *const u16, args: *const u16, dir: *const u16, show: i32) -> isize; }
        let exe = std::env::current_exe().map_err(|e| e.to_string())?;
        let file: Vec<u16> = exe.as_os_str().encode_wide().chain(Some(0)).collect();
        let verb: Vec<u16> = "runas".encode_utf16().chain(Some(0)).collect();
        let arguments: Vec<u16> = format!("--wait-for-pid {}", std::process::id()).encode_utf16().chain(Some(0)).collect();
        let result = unsafe { ShellExecuteW(0, verb.as_ptr(), file.as_ptr(), arguments.as_ptr(), std::ptr::null(), 1) };
        if result <= 32 { return Err("Administrator restart canceled or failed".into()); }
        app.exit(0);
        Ok(())
    }
    #[cfg(not(windows))] { let _ = app; Err("Administrator restart is available on Windows".into()) }
}
fn main() {
    // Wait for the unelevated instance to release its single-instance lock.
    #[cfg(windows)] {
        let args: Vec<String> = std::env::args().collect();
        if args.get(1).map(String::as_str) == Some("--wait-for-pid") {
            if let Some(pid) = args.get(2).and_then(|p| p.parse::<u32>().ok()) {
                #[link(name = "kernel32")] extern "system" { fn OpenProcess(access: u32, inherit: i32, pid: u32) -> isize; fn WaitForSingleObject(handle: isize, ms: u32) -> u32; fn CloseHandle(handle: isize) -> i32; }
                unsafe { let handle = OpenProcess(0x00100000, 0, pid); if handle != 0 { WaitForSingleObject(handle, 15000); CloseHandle(handle); } }
            }
        }
    }
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _, _| show(app)))
        .plugin(tauri_plugin_dialog::init()).plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![bridge_request, set_language, open_resource, export_diagnostics, choose_smartctl, restart_as_admin])
        .setup(|app| {
            let handle = app.handle();
            let backend = match Service::start(handle) { Ok(service) => Backend { service: Some(service), error: String::new() }, Err(error) => Backend { service: None, error: error.to_string() } };
            app.manage(backend);
            let show_item = MenuItem::with_id(app, "show", "Show monitor", true, None::<&str>)?;
            let quit_item = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show_item, &quit_item])?;
            TrayIconBuilder::new().icon(app.default_window_icon().unwrap().clone()).menu(&menu)
                .on_menu_event(|app, event| match event.id.as_ref() { "show" => show(app), "quit" => app.exit(0), _ => {} })
                .on_tray_icon_event(|tray, event| if matches!(event, TrayIconEvent::DoubleClick { .. }) { show(tray.app_handle()); }).build(app)?;
            app.manage(TrayLabels { show: show_item, quit: quit_item });
            Ok(())
        })
        .on_window_event(|window, event| if let tauri::WindowEvent::CloseRequested { api, .. } = event { api.prevent_close(); let _ = window.hide(); })
        .build(tauri::generate_context!()).expect("Could not initialize Divoom monitor");
    app.run(|handle, event| if matches!(event, tauri::RunEvent::Exit) {
        if let Some(backend) = handle.try_state::<Backend>() { if let Some(service) = &backend.service { service.stop(); } }
    });
}
