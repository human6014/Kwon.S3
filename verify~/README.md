# 검증 레시피

패키지만으로는 컴파일을 확인할 수 없다(Unity 프로젝트가 있어야 asmdef 가 빌드된다).
빈 프로젝트를 하나 만들어 물려보는 절차를 남겨 둔다. **이 폴더는 Unity 가 읽지 않는다** —
폴더 이름이 `~` 로 끝나면 Unity 가 통째로 무시한다.

```bash
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Unity.exe"
P=/c/UnityProjectSet/Unity/_PkgCheck

# 1. 최소 프로젝트 스켈레톤 (나머지는 Unity 가 만든다)
mkdir -p "$P/ProjectSettings" "$P/Packages" "$P/Assets/Editor" "$P/Logs"
echo "m_EditorVersion: 6000.3.20f1" > "$P/ProjectSettings/ProjectVersion.txt"
cp "verify~/manifest.sample.json" "$P/Packages/manifest.json"
cp "verify~/PkgCheck.cs" "$P/Assets/Editor/PkgCheck.cs"

# 2. 컴파일 — 종료 코드는 못 믿으니 로그를 grep 한다
"$UNITY" -batchmode -quit -projectPath "$P" -logFile "$P/Logs/compile.log" -nographics
grep -c "error CS" "$P/Logs/compile.log"          # 0 이어야 한다
ls "$P/Library/ScriptAssemblies/" | grep KwonStudio # dll 4개

# 3. 설정 에셋 생성 → 새 프로세스에서 Resources 로 다시 읽기
"$UNITY" -batchmode -projectPath "$P" -executeMethod PkgCheck.Seed   -logFile "$P/Logs/seed.log"   -nographics
"$UNITY" -batchmode -projectPath "$P" -executeMethod PkgCheck.Verify -logFile "$P/Logs/verify.log" -nographics
grep PKGCHECK "$P/Logs/verify.log"                 # PKGCHECK OK
```

`Verify` 를 **별도 프로세스**로 돌리는 게 핵심이다. 같은 프로세스면 `S3Settings.Instance` 가
`GetOrCreate` 가 채워 둔 캐시를 그대로 돌려줘서 `Resources.Load` 경로를 검증하지 못한다.

## 마지막 실행 결과 (2026-08-31, Unity 6000.3.20f1)

- 컴파일: error 0 / warning 0, 어셈블리 4개 생성
- `PKGCHECK url=https://example.invalid/config/v1/tables.json id=SHEET_ID_TEST docs=2:SHEET_ID_TEST,LOC_ID_TEST`
- `PKGCHECK OK`
