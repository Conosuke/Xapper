#!/bin/bash
#
# Xapper CLI를 사용한 통합테스트 러너 예시
#
# 사용법:
#   ./runner.sh /path/to/app.exe ./scripts ./results
#

set -e

APP_EXE="${1:?Usage: runner.sh <app.exe> [scripts_dir] [results_dir]}"
SCRIPTS_DIR="${2:-.}"
RESULTS_DIR="${3:-./results}"

# 색상
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 결과 디렉토리 생성
mkdir -p "$RESULTS_DIR"

CLI_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
XAPPER_CLI="$CLI_DIR/bin/xapper.js"

echo -e "${BLUE}🚀 Xapper 통합테스트 시작${NC}"
echo "앱: $APP_EXE"
echo "스크립트: $SCRIPTS_DIR"
echo ""

# 1. 앱 시작
echo -e "${BLUE}📱 앱 시작 중...${NC}"
LAUNCH_JSON=$(node "$XAPPER_CLI" launch --exe "$APP_EXE" 2>/dev/null || echo '{"error":"Launch failed"}')
APP_PID=$(echo "$LAUNCH_JSON" | jq -r '.pid // .error' 2>/dev/null)

if [[ "$APP_PID" == *"error"* ]] || [ -z "$APP_PID" ]; then
  echo -e "${RED}앱 시작 실패${NC}"
  exit 1
fi

echo -e "${GREEN}✅ 앱 시작됨 (PID: $APP_PID)${NC}"

# 테스트 결과 추적
PASS=0
FAIL=0
ERROR=0
TOTAL=0

declare -a FAILED_TESTS

# 2. 테스트 실행
echo ""
for script_file in "$SCRIPTS_DIR"/*.js; do
  if [ ! -f "$script_file" ]; then
    continue
  fi

  TEST_NAME=$(basename "$script_file" .js)
  RESULT_FILE="$RESULTS_DIR/${TEST_NAME}.json"

  ((TOTAL++))

  echo -e "${BLUE}🧪 테스트 실행: $TEST_NAME${NC}"

  if node "$XAPPER_CLI" run "$script_file" > "$RESULT_FILE" 2>&1; then
    echo -e "${GREEN}✅ $TEST_NAME 통과${NC}"
    ((PASS++))
  else
    echo -e "${RED}❌ $TEST_NAME 실패${NC}"
    ((FAIL++))
    FAILED_TESTS+=("$TEST_NAME")
  fi

  sleep 0.5
done

# 3. 최종 보고서
echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${BLUE}📋 테스트 결과 요약${NC}"
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"

echo "총 테스트: $TOTAL"
echo -e "${GREEN}✅ 통과: $PASS${NC}"
echo -e "${RED}❌ 실패: $FAIL${NC}"
echo -e "${YELLOW}⚠️  오류: $ERROR${NC}"
echo ""

# 실패 목록
if [ ${#FAILED_TESTS[@]} -gt 0 ]; then
  echo "실패한 테스트:"
  for test in "${FAILED_TESTS[@]}"; do
    echo "  - $test"
  done
  echo ""
fi

# 최종 결과 파일 저장
SUMMARY_FILE="$RESULTS_DIR/summary.json"
cat > "$SUMMARY_FILE" <<EOF
{
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "appPid": "$APP_PID",
  "total": $TOTAL,
  "passed": $PASS,
  "failed": $FAIL,
  "errors": $ERROR
}
EOF

echo -e "${GREEN}📊 결과 저장됨: $SUMMARY_FILE${NC}"

# 종료 코드
if [ $FAIL -gt 0 ] || [ $ERROR -gt 0 ]; then
  exit 1
else
  exit 0
fi
