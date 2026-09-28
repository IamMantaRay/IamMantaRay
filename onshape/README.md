# Liquid Capacity (Onshape FeatureScript)

컨테이너(컵, 병, 탱크 등)에 담을 수 있는 액체 용량을 측정하는 커스텀 피처입니다.

## 사용 방법
1. Onshape 문서에서 **+ → Create Feature Studio** 로 새 Feature Studio를 만듭니다.
2. 자동 생성된 첫 두 줄(`FeatureScript xxxx;` / `import(...)`)은 그대로 두고, 나머지에 `LiquidCapacity.fs` 의 본문을 붙여넣습니다.
   (버전 번호가 다르면 Feature Studio가 만든 번호를 사용하세요.)
3. 저장 후 Part Studio 툴바의 **Custom features** 에서 *Liquid Capacity* 를 추가합니다.

## 입력값
| 항목 | 설명 |
|---|---|
| Container | 측정할 컨테이너 솔리드 (여러 개 선택 가능 — 예: 병 + 뚜껑) |
| Up direction | 액체면의 "위" 방향. 평면/모서리/축/메이트 커넥터. 비우면 +Z |
| Fill mode | **Full**: 가득(입구/최상단까지) · **Percent**: 내부 깊이의 % · **Liquid level**: 컨테이너 바닥에서의 액면 높이 |
| Liquid density | 질량 계산용 밀도 (g/mL, 물 = 1) |
| Keep liquid body | 액체를 반투명 파란색 파트("Liquid")로 남김 |
| Show fill steps | 액체를 깊이 단계별 층(band)으로 잘라 아래(연한 하늘색)→수면(진한 파란색) 그라데이션으로 표시 |
| Number of steps | 단계 수 (기본 10 → 10% 간격). 층 파트 이름은 `Liquid 30-40%` 형식 |
| Store as variable | 결과 부피를 변수로 저장 (`#liquidVolume` 로 다른 피처에서 참조) |

결과는 피처 이름(`Liquid capacity (xxx)`), 피처 정보 메시지, FeatureScript 콘솔에 mL / L / g 으로 표시됩니다.

부분 채움(Percent / Liquid level)일 때는 **전체 용량 대비 %** 도 함께 표시됩니다.

## 채워지는 모습 보기
- **Show fill steps** 를 켜면 단계는 *전체 내부 깊이* 기준으로 나뉘므로, 예를 들어 60% 채움이면 10개 중 아래 6개 층만 보입니다.
  FeatureScript 콘솔에는 높이별 누적 용량 표가 출력됩니다.
  ```
  Fill table (height from inner bottom | % of depth | cumulative volume | % of capacity)
    9.5 mm | 10.0% | 21.3 mL | 7.8%
    19.0 mm | 20.0% | 44.1 mL | 16.1%
    ...
  ```
- 피처 대화창에서 *Fill (%)* 나 *Liquid level* 값을 바꾸면 액체 높이가 즉시 다시 계산되어 보입니다.
- 여러 수위를 빠르게 넘겨 보려면 **Configuration**(예: 변수 `fill` = 20, 40, 60, 80, 100)을 만들고
  *Fill (%)* 입력에 `#fill` 을 넣은 뒤 구성을 바꿔 보세요.

## 동작 원리
컨테이너보다 약간 큰 박스를 액면 높이까지 만들고 컨테이너를 빼면(Boolean subtract) 박스가 여러 조각으로 나뉩니다.
박스의 옆면/바닥면에 닿는 조각은 바깥 공기이므로 버리고, 벽으로 둘러싸인 조각만 액체로 간주해 부피를 계산합니다.
원본 컨테이너는 수정되지 않습니다.

## 주의
- 이중벽 사이의 밀폐 공간 등 **닫힌 빈 공간은 모두 액체로 합산**됩니다(메시지에 캐비티 개수 표시).
- 주둥이나 손잡이가 입구보다 높으면 *Full* 모드에서 "overflow" 오류가 날 수 있습니다 — *Liquid level* 모드로 입구 높이를 지정하세요.
