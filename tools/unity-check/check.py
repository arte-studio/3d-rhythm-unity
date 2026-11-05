"""
Unity を起動せずに Assets/Rhythm の C#（Core・Runtime・Editor）がコンパイルできるかを確かめる．

Unity が作る csproj（3d-rhythmgame/Arte.Rhythm.*.csproj．git には入れていない）から参照する DLL と
定義済みの記号（UNITY_EDITOR など）だけを借り，.cs はフォルダから全部拾う．
なので .cs を足したり動かしたりしても，Unity を開き直さずにそのまま確かめられる．

使い方（リポジトリの一番上で）:
    python tools/unity-check/check.py            # エディタ向け（UNITY_EDITOR あり）
    python tools/unity-check/check.py --android  # Quest 単体向け（UNITY_ANDROID あり，UNITY_EDITOR なし．Editor のコードは除く）

前提: 一度 Unity でプロジェクトを開いて csproj と Library/ScriptAssemblies ができていること．
Core だけなら Unity 無しの dotnet test 3d-rhythmgame/CoreTests~ でも確かめられる（Unity の DLL を使わないことも確かめられる）．
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
UNITY = os.path.join(REPO, "3d-rhythmgame")
RHYTHM = os.path.join(UNITY, "Assets", "Rhythm")
# 参照を借りる csproj（Editor が Runtime・Core を含むが，念のため全部合わせる）
SOURCES = ["Arte.Rhythm.Editor.csproj", "Arte.Rhythm.Runtime.csproj", "Arte.Rhythm.Core.csproj"]


def read_csproj(path):
    refs, defines = {}, set()
    root = ET.parse(path).getroot()
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "Reference":
            hint = next((c.text for c in el if c.tag.split("}")[-1] == "HintPath"), None)
            name = el.get("Include")
            if hint and name and not name.startswith("Arte.Rhythm"):
                if not os.path.isabs(hint):
                    hint = os.path.join(UNITY, hint)
                refs[name] = hint
        elif tag == "DefineConstants" and el.text:
            defines.update(d for d in el.text.split(";") if d)
    return refs, defines


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--android", action="store_true", help="Quest 単体（Android）のつもりで確かめる")
    args = ap.parse_args()

    refs, defines = {}, set()
    for name in SOURCES:
        p = os.path.join(UNITY, name)
        if not os.path.exists(p):
            sys.exit(f"{p} が無い．一度 Unity でプロジェクトを開いて csproj を作る")
        r, d = read_csproj(p)
        refs.update(r)
        defines |= d

    folders = ["Core", "Runtime"]
    if args.android:
        defines = {d for d in defines if not (d.startswith("UNITY_EDITOR") or d.startswith("UNITY_STANDALONE"))}
        defines |= {"UNITY_ANDROID", "PLATFORM_ANDROID"}
    else:
        folders.append("Editor")

    missing = [n for n, h in refs.items() if not os.path.exists(h)]
    if missing:
        print(f"注意: 見つからない DLL が {len(missing)} 個（Unity を開き直すと直ることがある）: {', '.join(missing[:5])}")

    out = os.path.join(tempfile.gettempdir(), "arte-unity-check" + ("-android" if args.android else ""))
    os.makedirs(out, exist_ok=True)
    compile_items = "\n".join(
        f'    <Compile Include="{os.path.join(RHYTHM, f)}{os.sep}**{os.sep}*.cs" />' for f in folders)
    ref_items = "\n".join(
        f'    <Reference Include="{n}"><HintPath>{h}</HintPath><Private>false</Private></Reference>'
        for n, h in sorted(refs.items()) if os.path.exists(h))
    csproj = f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
    <NoStdLib>true</NoStdLib>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>{';'.join(sorted(defines))}</DefineConstants>
    <NoWarn>CS0169;CS0414;CS0649;CS1998</NoWarn>
    <OutputPath>{os.path.join(out, 'bin')}</OutputPath>
    <BaseIntermediateOutputPath>{os.path.join(out, 'obj')}{os.sep}</BaseIntermediateOutputPath>
    <AssemblyName>ArteRhythmCheck</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
{compile_items}
  </ItemGroup>
  <ItemGroup>
{ref_items}
  </ItemGroup>
</Project>
"""
    proj = os.path.join(out, "ArteRhythmCheck.csproj")
    with open(proj, "w", encoding="utf-8") as f:
        f.write(csproj)
    r = subprocess.run(["dotnet", "build", proj, "-nologo", "-v:q", "-clp:NoSummary"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    lines = sorted({re.sub(r"\s*\[[^\]]*\.csproj\]$", "", l.strip()) for l in r.stdout.splitlines() if ": error " in l})
    for l in lines:
        print(l.replace(RHYTHM + os.sep, "Assets/Rhythm/"))
    what = "Quest 単体（Android）" if args.android else "エディタ"
    if r.returncode == 0:
        print(f"OK: {what}向けにコンパイルできた（{', '.join(folders)}）")
    else:
        if not lines:
            print(r.stdout[-3000:])
        print(f"NG: {what}向けのコンパイルに失敗（エラー {len(lines)} 個）")
    sys.exit(r.returncode)


if __name__ == "__main__":
    main()
