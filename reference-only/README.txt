Reference assemblies go here.

They are deliberately NOT in this repository: they belong to Decal and to Virindi, not to this
project, and redistributing them is not ours to do. The build needs them only to compile against.

build.bat looks for an installed Decal and Virindi View Service first and builds against those, so
if you have both installed you do not need to do anything and this folder can stay empty.

Otherwise copy these four files in here from your own install:

  Decal.Adapter.dll         from your Decal folder, e.g. C:\Program Files (x86)\Decal 3.0
  Decal.Interop.Core.dll    same folder
  Decal.Interop.Inject.dll  same folder
  VirindiViewService.dll    from your VirindiViewService plugin folder

DO NOT copy them into your Decal folder, your plugin folder, or anywhere the game loads from -
Decal and VVS supply their own at runtime, and a stale duplicate next to the plugin is a reliable
way to get version mismatches and a plugin that refuses to load.

If the compiler reports CS0012 about a Decal.Interop.* assembly that is not listed above, build
against a real Decal install instead: build.bat finds one automatically, or set DecalPath in
DreamweaveMarket.csproj.
