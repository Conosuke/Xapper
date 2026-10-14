# xapper-cli

Xapper MCP 서버의 CLI 인터페이스입니다. WPF 애플리케이션 테스트 자동화를 위한 명령줄 도구입니다.

## 설치

```bash
npm install
npm link  # global로 등록 (선택사항)
```

## 사용법

### run — 스크립트 실행

```bash
xapper run script.js
```

결과를 JSON 배열로 stdout에 출력합니다:

```json
[
  {
    "stepIndex": 0,
    "tool": "xapper_click",
    "status": "ok",
    "result": "Clicked via routed event"
  },
  {
    "stepIndex": 1,
    "tool": "xapper_type",
    "status": "ok",
    "result": "Typed: hello"
  }
]
```

### launch — 앱 시작

```bash
xapper launch --exe "C:\MyApp\MyApp.exe"
```

출력:
```json
{
  "pid": 12345
}
```

### UI 명령어

#### click — 요소 클릭

```bash
xapper click --target "name=LoginButton"
xapper click --target "name=OrderGrid" --x 0.5 --y 0.5
```

#### type — 텍스트 입력

```bash
xapper type --target "name=UsernameBox" --text "admin"
```

#### key — 키 입력

```bash
xapper key --key "Enter"
xapper key --key "Ctrl+A"
```

#### drag — 드래그

```bash
xapper drag --target "name=TreeNode" --fromX 0.5 --fromY 0.5 --toX 0.2 --toY 0.8
```

#### select — 옵션 선택 (ComboBox/ListBox)

```bash
xapper select --target "name=LanguageBox" --value "Korean"
```

#### screenshot — 스크린샷

```bash
xapper screenshot --ref 5 --annotate
```

#### snapshot — 요소 내용 텍스트로

```bash
xapper snapshot --ref 5
```

## E2E 테스트에서 사용

### PowerShell 예시

```powershell
# 앱 시작
$launchResult = node xapper-cli.js launch --exe "C:\MyApp.exe" | ConvertFrom-Json
$pid = $launchResult.pid

# 스크립트 실행
$result = node xapper-cli.js run "my-test.js" | ConvertFrom-Json

# 결과 저장
$result | ConvertTo-Json | Out-File "results/test-output.json"

# 실패 여부 확인
if ($LASTEXITCODE -ne 0) {
  Write-Host "Test failed!"
  exit 1
}
```

### Bash 예시

```bash
#!/bin/bash

# 앱 시작
LAUNCH_RESULT=$(node xapper-cli.js launch --exe /opt/MyApp/MyApp.exe)
PID=$(echo $LAUNCH_RESULT | jq -r '.pid')

# 스크립트 실행
RESULT=$(node xapper-cli.js run "my-test.js")
EXIT_CODE=$?

# 결과 저장
echo "$RESULT" | jq . > results/test-output.json

# 실패 여부 확인
if [ $EXIT_CODE -ne 0 ]; then
  echo "Test failed!"
  exit 1
fi
```

## 상태 유지

명령마다 새로운 McpServer 인스턴스가 시작됩니다. 앱 프로세스는 계속 실행되므로 상태가 유지됩니다.

**주의:** 각 명령 사이에 약간의 지연이 있습니다 (서버 시작 오버헤드 ~500ms).

연속된 명령 실행 시 성능을 원하면 데몬 모드를 고려하세요 (향후 구현).

## 에러 처리

실패 시 exit code는 1입니다:

```bash
node xapper-cli.js click --target invalid
echo $?  # 1
```

stderr에는 JSON 형식의 에러 정보가 출력됩니다:

```json
{
  "status": "error",
  "error": "Element not found: invalid"
}
```

## 환경 변수

- `DOTNET_PATH` — dotnet 실행 경로 (기본값: `dotnet`)
- `DEBUG` — 설정하면 stack trace 출력

```bash
DEBUG=1 xapper run script.js
DOTNET_PATH="C:\Program Files\dotnet\dotnet.exe" xapper launch --exe app.exe
```

## 개발

### 테스트 실행

```bash
npm test
```

### 로그 보기

```bash
DEBUG=1 node bin/xapper.js run test.js 2>&1 | head -50
```
