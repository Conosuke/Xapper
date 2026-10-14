# Xapper Tools Specification v0.1

모든 Xapper 도구의 정식 명세입니다. MCP 서버, CLI, 향후 다른 인터페이스는 이 명세를 따릅니다.

---

## 응답 형식

### 성공 (status = "ok")
```json
{
  "status": "ok",
  "result": "..." or {...}
}
```

### 실패 (status = "error")
```json
{
  "status": "error",
  "error": "설명 (첫 글자 대문자)"
}
```

### 타임아웃 (status = "timeout")
```json
{
  "status": "timeout",
  "error": "Tool name stayed ...: <원인>"
}
```

---

## 프로세스 관리

### xapper_launch
앱 시작. 훅을 주입한 후 자동 연결.

**인자:**
- `exePath` (string, 필수): 앱의 절대 경로
- `args` (string, 선택): 명령줄 인자
- `timeoutMs` (int, 선택, 기본 15000): 시작 대기 시간 (1000~300000)

**응답:**
```json
{
  "status": "ok",
  "result": "Started PID 1234."
}
```

**실패 원인:**
- `no file at <path>` — 파일 없음
- `could not start <name> (...)` — 시작 실패
- `exited with code X before the inspector came up` — 앱이 즉시 종료
- `timeout` — 훅 로드 시간 초과

---

### xapper_attach
실행 중인 프로세스에 훅 주입.

**인자:**
- `processId` (int, 필수): 프로세스 ID
- `timeoutMs` (int, 선택, 기본 15000): 주입 대기 시간

**응답:**
```json
{
  "status": "ok",
  "result": "Attached to <name> (PID 1234)."
}
```

**실패 원인:**
- `no process with ID X` — PID 없음
- `is not a WPF app` — WPF가 아님
- `already attached` — 이미 연결됨
- `timeout` — 훅 로드 시간 초과

---

### xapper_detach
현재 세션 분리 (앱 닫지 않음).

**인자:** 없음

**응답:**
```json
{
  "status": "ok",
  "result": "Detached from <name>."
}
```

---

### xapper_list_processes
실행 중인 WPF 프로세스 목록.

**인자:** 없음

**응답:**
```json
{
  "status": "ok",
  "result": [
    {
      "processId": 1234,
      "name": "MyApp",
      "title": "My Application - Document.txt"
    }
  ]
}
```

---

## UI 동작

### xapper_click
요소 클릭 (또는 화면 좌표).

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref 번호
- `x` (float, 선택): 상대 좌표 (0.0~1.0, 요소 기준)
- `y` (float, 선택): 상대 좌표
- `modifiers` (string, 선택): "Ctrl", "Shift", "Alt", "Ctrl+Shift" 등
- `timeout` (int, 선택, 기본 5000): 요소 대기 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Clicked via <path>.<warning>"
}
```

`<path>`: `accessibility`, `button event`, `routed event`, `real mouse` 중 하나  
`<warning>`: 부연 설명 (예: "not foreground" 등) 또는 빈 문자열

**응답 (실패):**
```json
{
  "status": "timeout",
  "error": "xapper_click stayed hidden: element not found after 5000ms"
}
```

**실패 원인:**
- `stayed disabled` — 요소가 비활성화됨
- `stayed hidden` — 화면에 보이지 않음
- `was still loading` — UI가 정착하지 않음
- `cannot be resolved` — ref가 유효하지 않음
- `matches N elements` — 선택자가 모호함

---

### xapper_doubleclick
더블클릭.

**인자:** xapper_click과 동일

**응답:** xapper_click과 동일

---

### xapper_rightclick
우클릭.

**인자:** xapper_click과 동일

**응답:** xapper_click과 동일

---

### xapper_type
텍스트 입력.

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref
- `text` (string, 필수): 입력할 텍스트
- `timeout` (int, 선택, 기본 5000): 요소 대기 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Typed: <text>"
}
```

**실패 원인:** xapper_click과 동일 + "does not support text input"

---

### xapper_key
키 입력.

**인자:**
- `key` (string, 필수): "Enter", "Escape", "F1", "Ctrl+C", "Ctrl+Shift+S" 등
- `modifiers` (string, 선택): "Ctrl", "Shift", "Alt"
- `timeout` (int, 선택, 기본 0): 대기 시간 (0 = 즉시)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Sent: <key>"
}
```

---

### xapper_select
선택 (ComboBox, ListBox 등).

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref
- `value` (string, 필수): 선택할 값 (텍스트)
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Selected: <value>"
}
```

**실패 원인:** "does not support selection" 등

---

### xapper_toggle
토글 (CheckBox, ToggleButton 등).

**인자:**
- `target` (string|int, 필수)
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Toggled to: <true|false>"
}
```

---

### xapper_expand
트리 노드 확장 (TreeViewItem 등).

**인자:**
- `target` (string|int, 필수)
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Expanded."
}
```

---

### xapper_scroll
스크롤.

**인자:**
- `target` (string|int, 필수): 스크롤바 또는 ItemsControl
- `direction` (string, 필수): "up", "down", "left", "right"
- `amount` (int, 선택, 기본 3): 스크롤 량
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Scrolled <direction> by <amount>"
}
```

---

### xapper_drag
드래그. 요소 기반 또는 화면 좌표 기반.

**인자:**
- `source` (string|int, 선택): 시작 요소
- `sourceX` (float, 선택): 시작 화면 좌표 X (픽셀)
- `sourceY` (float, 선택): 시작 화면 좌표 Y
- `target` (string|int, 선택): 끝 요소
- `targetX` (float, 선택): 끝 화면 좌표 X
- `targetY` (float, 선택): 끝 화면 좌표 Y
- `offsetX` (float, 선택): 상대 이동 X (source 기준)
- `offsetY` (float, 선택): 상대 이동 Y
- `modifiers` (string, 선택): "Ctrl" 등
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Dragged via <thumb|synthetic|real mouse>"
}
```

**필수 조합:**
- `source` XOR (`sourceX` + `sourceY`)
- `target` OR `offsetX/offsetY` OR (`targetX` + `targetY`)

---

## 읽기

### xapper_find
요소 검색.

**인자:**
- `target` (string, 필수): 선택자
- `timeout` (int, 선택, 기본 2000): 검색 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": [
    {
      "ref": 1,
      "type": "Button",
      "name": "OkButton",
      "text": "OK",
      "visible": true,
      "enabled": true,
      "bounds": {"x": 100, "y": 200, "width": 60, "height": 30}
    }
  ]
}
```

---

### xapper_snapshot
요소 내용 텍스트로.

**인자:**
- `ref` (int, 선택): 요소 ref (없으면 전체)
- `limit` (int, 선택, 기본 1000): 최대 줄 수

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "요소 내용\n여러 줄\n..."
}
```

---

### xapper_get_property
요소 속성 읽기.

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref
- `property` (string, 필수): 속성명 (예: "Text", "IsChecked")
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "속성값"
}
```

---

### xapper_assert
값 검증.

**인자:**
- `target` (string|int, 필수)
- `property` (string, 필수)
- `expected` (string|number|boolean, 필수)
- `timeout` (int, 선택, 기본 5000): 조건 대기 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Assertion passed: <property> == <expected>"
}
```

**응답 (실패):**
```json
{
  "status": "error",
  "error": "Assertion failed: Text == 'OK' (got 'Cancel')"
}
```

---

### xapper_screenshot
스크린샷 캡처.

**인자:**
- `ref` (int, 선택): 요소 ref (없으면 전체 데스크톱)
- `annotate` (boolean, 선택): true = 클릭 가능 요소에 숫자 표시
- `path` (string, 선택): 저장 경로 (없으면 메모리만)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": {
    "width": 1920,
    "height": 1080,
    "imageBase64": "iVBORw0KGgoAAAANS...",
    "path": "C:\\results\\screenshot.png"
  }
}
```

---

### xapper_element_at
화면 좌표의 요소 찾기.

**인자:**
- `x` (int, 필수): 화면 좌표 X (픽셀)
- `y` (int, 필수): 화면 좌표 Y

**응답:**
```json
{
  "status": "ok",
  "result": {
    "ref": 5,
    "type": "Button",
    "name": "SaveButton",
    "text": "Save"
  }
}
```

---

### xapper_events
앱이 발생시킨 이벤트 목록.

**인자:**
- `count` (int, 선택, 기본 20): 반환할 최근 이벤트 수
- `sinceRef` (int, 선택): 이 ref 이후의 이벤트만

**응답:**
```json
{
  "status": "ok",
  "result": [
    {
      "type": "SelectionChanged",
      "source": "OrderGrid",
      "timestamp": "2026-10-14T10:30:45Z"
    },
    {
      "type": "TextChanged",
      "source": "SearchBox",
      "value": "test",
      "timestamp": "2026-10-14T10:30:46Z"
    }
  ]
}
```

---

## 스크립트 실행

### xapper_run
JavaScript 스크립트 실행.

**인자:**
- `script` (string, 선택): JavaScript 코드
- `scriptPath` (string, 선택): 스크립트 파일 경로
- `timeoutMs` (int, 선택, 기본 60000): 제한 시간 (1000~300000)
  - 시간 초과 시 스크립트는 강제 중단됨 (무한루프 포함)
  - Jint 엔진의 취소 토큰 + Task.WhenAny 이중 타임아웃으로 보장

`script` 또는 `scriptPath` 중 정확히 하나만 제공.

**응답 (성공):**
```json
{
  "status": "ok",
  "result": {
    "outcome": "success",
    "duration": 1234,
    "returnValue": null,
    "log": ["Step 1", "Step 2"]
  }
}
```

**응답 (실패):**
```json
{
  "status": "ok",
  "result": {
    "outcome": "fail",
    "duration": 500,
    "line": 10,
    "error": "Element not found",
    "trace": "at click (line 10)",
    "screenshot": "C:\\results\\failure.png"
  }
}
```

**응답 (타임아웃):**
```json
{
  "status": "ok",
  "result": {
    "outcome": "timeout",
    "duration": 60000,
    "error": "Script exceeded 60000ms"
  }
}
```

**스크립트 내 전역 객체:**
```javascript
xapper = {
  // 읽기
  find(selector) → [{ref, type, name, ...}],
  one(selector) → element (첫 번째),
  get(target, property) → value,
  assert(target, property, expected, {timeout}) → void,
  
  // 동작
  click(target, {x, y, modifiers, timeout}) → void,
  doubleClick(target, {x, y, modifiers, timeout}) → void,
  rightClick(target, {x, y, modifiers, timeout}) → void,
  type(target, text) → void,
  key(keys, {modifiers}) → void,
  select(target, value) → void,
  toggle(target) → void,
  expand(target) → void,
  scroll(target, direction, {amount}) → void,
  drag(source, {sourceX, sourceY, targetX, targetY, offsetX, offsetY, modifiers, timeout}) → void,
  
  // 검증
  waitUntil(selector, {timeout}) → void,
  screenshot({path, annotate}) → void,
  events({count, sinceRef}) → [...],
  
  // 유틸
  sleep(ms) → void,
  log(...args) → void,
  fail(message) → void
};

notice = {
  show(message) → void,
  hide() → void
};
```

---

### xapper_batch
여러 도구를 한 번에 호출.

**인자:**
- `steps` (array, 필수): 도구 호출 배열

**각 step:**
```json
{
  "tool": "xapper_click",
  "target": "name=Button",
  "x": 0.5,
  "y": 0.5
}
```

**응답 (성공):**
```json
{
  "status": "ok",
  "result": [
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
}
```

실패한 step에서 배치 중단. 이후 step은 실행되지 않음.

---

## 화면 기록

### xapper_screen_learn
현재 화면을 기억.

**인자:**
- `name` (string, 필수): 화면 이름 (예: "login-screen")
- `notes` (string, 선택): 주의사항

**응답:**
```json
{
  "status": "ok",
  "result": "Learned screen 'login-screen' with 12 selectors"
}
```

---

### xapper_screen_recall
기억한 화면의 선택자 반환.

**인자:**
- `name` (string, 필수): 화면 이름

**응답:**
```json
{
  "status": "ok",
  "result": "name=UsernameBox    TextBox\nname=PasswordBox    PasswordBox\n..."
}
```

---

### xapper_screen_note
화면에 주의사항 추가.

**인자:**
- `name` (string, 필수): 화면 이름
- `note` (string, 필수): 추가할 주의사항
- `replace` (boolean, 선택): true = 기존 노트 교체, false = 추가

**응답:**
```json
{
  "status": "ok",
  "result": "Added note to 'login-screen'"
}
```

---

### xapper_screen_forget
기억한 화면 삭제.

**인자:**
- `name` (string, 필수)

**응답:**
```json
{
  "status": "ok",
  "result": "Forgot screen 'login-screen'"
}
```

---

## Win32 도구 (향후)

### xapper_file_dialog
파일 열기 대화 자동 응답.

**인자:**
- `filePath` (string, 필수): 선택할 파일 경로
- `timeout` (int, 선택, 기본 5000)

**응답:**
```json
{
  "status": "ok",
  "result": "FileDialog dismissed with: C:\\path\\to\\file.txt"
}
```

---

### xapper_message_box
메시지 박스 자동 응답.

**인자:**
- `button` (string, 필수): "OK", "Yes", "No", "Cancel" 등
- `timeout` (int, 선택, 기본 5000)

**응답:**
```json
{
  "status": "ok",
  "result": "MessageBox dismissed with: Yes"
}
```

---

### xapper_list_windows
열린 창 목록 (캡처 포함).

**인자:** 없음

**응답:**
```json
{
  "status": "ok",
  "result": [
    {
      "hwnd": "0x00010A50",
      "title": "Save As",
      "className": "FileDialogInternal",
      "visible": true,
      "imageBase64": "iVBORw0KGgoAAAA..."
    }
  ]
}
```

---

## 선택자 문법

```
"name=ButtonName"           — x:Name 속성
"id=SaveButton"             — x:Name (id는 별칭)
"text=Click Me"             — TextBlock, Button Content 등
"type=Button"               — 타입명
"text=로그인,type=Button"   — AND (쉼표)

ref 숫자                     — find 응답의 ref 직접 사용

element 객체                 — find/one 결과 직접 사용
```

---

## 응답 규칙

1. **성공:** `status: "ok"` + `result`
2. **실패:** `status: "error"` + `error` (자세한 설명)
3. **타임아웃:** `status: "timeout"` + `error` ("stayed <원인>")
4. **배치 중단:** 첫 실패 step에서 중단, 이후 step은 skip

---

## 버전 히스토리

### v0.1 (2026-10-14)
- 초안 작성
- 기존 도구 명세화
- Win32 도구는 향후 추가 예정
