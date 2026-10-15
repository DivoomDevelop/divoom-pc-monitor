param([string]$Destination = (Join-Path $PSScriptRoot '../macos/Assets'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach ($name in @('icon.png', 'icon.icns')) { if (Test-Path -LiteralPath (Join-Path $Destination $name)) { throw "Icon already exists: $name" } }
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
public static class DivoomMacIcon {
    static void U32(Stream s, uint n) { s.WriteByte((byte)(n>>24)); s.WriteByte((byte)(n>>16)); s.WriteByte((byte)(n>>8)); s.WriteByte((byte)n); }
    static void Chunk(Stream s, string name, byte[] bytes) {
        U32(s,(uint)bytes.Length); byte[] tag=Encoding.ASCII.GetBytes(name); s.Write(tag); s.Write(bytes);
        uint crc=0xffffffff; foreach(byte b in tag) crc=Step(crc,b); foreach(byte b in bytes) crc=Step(crc,b); U32(s,crc^0xffffffff);
    }
    static uint Step(uint crc,byte b) { crc^=b; for(int i=0;i<8;i++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u); return crc; }
    public static void Create(string directory) {
        const int size=512; using var raw=new MemoryStream();
        for(int y=0;y<size;y++) { raw.WriteByte(0); for(int x=0;x<size;x++) {
            int cx=Math.Clamp(x,90,421),cy=Math.Clamp(y,90,421); bool inside=(x-cx)*(x-cx)+(y-cy)*(y-cy)<=76*76;
            bool outer=x>=152&&x<=240&&y>=132&&y<=380 || x>=240&&((x-240)*(x-240)+(y-256)*(y-256)<=124*124);
            bool hole=x>=210&&((x-240)*(x-240)+(y-256)*(y-256)<=70*70);
            bool white=inside&&outer&&!hole; raw.WriteByte(white?(byte)255:(byte)18); raw.WriteByte(white?(byte)255:(byte)(60+y/8)); raw.WriteByte(white?(byte)255:(byte)(115+y/5)); raw.WriteByte(inside?(byte)255:(byte)0);
        }}
        using var compressed=new MemoryStream(); using(var z=new ZLibStream(compressed,CompressionLevel.SmallestSize,true)) z.Write(raw.ToArray());
        using var png=new MemoryStream(); png.Write(new byte[]{137,80,78,71,13,10,26,10});
        using var header=new MemoryStream(); U32(header,size); U32(header,size); header.Write(new byte[]{8,6,0,0,0});
        Chunk(png,"IHDR",header.ToArray()); Chunk(png,"IDAT",compressed.ToArray()); Chunk(png,"IEND",Array.Empty<byte>());
        byte[] image=png.ToArray(); File.WriteAllBytes(Path.Combine(directory,"icon.png"),image);
        using var icns=File.Create(Path.Combine(directory,"icon.icns")); icns.Write(Encoding.ASCII.GetBytes("icns")); U32(icns,(uint)(16+image.Length)); icns.Write(Encoding.ASCII.GetBytes("ic09")); U32(icns,(uint)(8+image.Length)); icns.Write(image);
    }
}
'@
[DivoomMacIcon]::Create([IO.Path]::GetFullPath($Destination))
