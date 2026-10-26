# 변경 기록 — DIGIMON TCG : DIGITAL COLOSSEUM

버전 번호는 [`src/DigimonTcg.Version.props`](src/DigimonTcg.Version.props) 한 곳에서 관리합니다.
앱 로비 오른쪽 아래와 창 제목에 `v0.8.1 (커밋 해시)` 형식으로 표시됩니다.

**규칙** (SemVer 0.x — 정식 출시 전)
- 새 기능 · 규칙 변경을 묶어 배포하면 **MINOR**를 올린다 (0.8.x → 0.9.0)
- 버그 수정만 배포하면 **PATCH**를 올린다 (0.8.0 → 0.8.1)
- 올린 커밋에 git 태그 `vX.Y.Z`를 붙이고, 이 파일 맨 위에 기록한다

---

## v0.28.0 — 2026-10-26
미지원 효과 문장 2차 묶음 — 도감 효과 문장 지원 7,017 → 7,279 / 10,366 (68.3% → 70.2%)
- 같은 도감(4,497장) 기준 7,017 → 7,228 / 10,279 (+211, 수정 전 지원되던 문장 중 깨진 것 0). 모든 절이 동작하는 카드 1,976 → 2,093장
- BT27 추가분 29장을 받아 도감 4,497 → 4,526장 (아래). 전체 기준 모든 절이 동작하는 카드 2,100장
- 진화원 시점 (엔진 — `GoldfishEngine.SourceEvents`): 효과 하나를 처리하기 전후의 진화원을 비교해, 그 효과가 끝난 뒤에 알린다
  - "When this card is trashed from digivolution cards by an effect" · "When an effect trashes this (digivolution) card" · "When effects trash this card from a [Mineral] or [Rock] trait Digimon's digivolution cards" (옵션 메인 칸의 같은 문장 포함)
  - "When one of your opponent's digivolution cards is trashed" · "When effects trash digivolution cards from your opponent's Digimon" · "When effects trash any of your [Mineral] or [Rock] trait Digimon's digivolution cards"
  - "When an effect places a digivolution card under this Digimon" · "When one of your effects places ~" · "When effects add to this Digimon's digivolution cards" · "When an effect places a Tamer card in this Digimon's digivolution cards" · "When Tamer cards are placed in ~" · "When effects place [CS] trait Digimon cards in ~" · "~ under any of your Digimon with ~"
  - 공식 Q&A: 진화(효과로 진화해도)로 겹친 카드는 늘어난 것이 아니다 (BT7-005) · 패로 되돌린 디지몬의 진화원은 "파기했다"가 아니다 (BT2-085 · P-004) · 자신의 효과로 파기해도 발휘 (BT10-006) · ≪마인드 링크≫로 놓은 테이머도 (BT17-003)
- ≪사용 조건≫ (옵션 21장): 공식 공지(2026-10-16 "＜Use Req.＞ 변경")대로 배틀 · 육성 에어리어의 지정된 디지몬 · 테이머가 색 조건을 충족한다 — 사용 가능 판정(엔진 · AI · 화면) 한 곳(`ActionValidator.MissingOptionColors`)에 반영. 생성 한글 설명문도 바뀐 공지대로 "지정된 조건이 색 조건을 충족한다"
- 정보가 적힌 토큰: "Play 1 [Familiar] Token. (Digimon/Yellow/3000 DP/[On Deletion] ~)" · "[Pipe Fox]" · "[Atho, René & Por]"(≪디코이《레드》/《블랙》≫) · "[Amon of Crimson Flame] Token (~) and 1 [Umon of Blue Thunder] Token (~)" · 디아블로몬 토큰 등 — 토큰의 키워드 · 효과도 모두 읽을 수 있을 때만
- 시큐리티 찾기: "Search your security stack, reveal 1 card among it and add it to your hand" · "~ for 1 ~, reveal it, and add it" · "~. You may add / play 1 ~ among them" · "Then, shuffle your security stack" · 조건 "If it's a yellow card" · "If you added" · "If you did"
- 공개: 보기만 하는 공개 "Reveal the top 5 cards of your or your opponent's deck. Return the revealed cards to the top or the bottom of the deck" · 공개 조건의 다른 표기 ("1 card with the [Hybrid] trait, or [Susanoomon] or [Takuya Kanbara]" · "1 [Four Great Dragons] trait or 1 yellow card" · "1 non-white Tamer card" · "1 [Angel], [Archangel] or [Fallen Angel] trait") · 원문 오타 3건
- 진화원: "Trash any 2 digivolution cards from your opponent's Digimon" · "~ of / under 1 of your opponent's Digimon" · "Trash its bottom 2 digivolution cards" · "Place ~ from your trash as this Digimon's bottom digivolution cards" · 비용 "placing ~ from your hand or trash as this Digimon's bottom digivolution card"
- 대상 표기: "[Mineral] or [Rock] trait Digimon" · "7000 DP or lower Digimon" (이름 앞에 둔 조건)
- 버그 수정
  - 카드 고르기 조건의 "or" — "with [Knightmon] in its name or [Twilight] trait"를 둘 다로 읽던 것을 둘 중 하나로 (BT10-092 · BT13-087 · BT20-057 · BT22-068 등, 화면 한글도 "~을 포함하거나 특징으로 ~를 가진")
  - "in any of its traits (other than [Sea Animal])" — 괄호를 지워 [Sea Animal]이 [Animal]로 잡히던 것을 제외 (BT13-048 · BT15-001 · BT23-012 등)
  - 비용 "by deleting 1 of your opponent's ~"를 자신의 디지몬을 소멸시키는 비용으로 읽을 수 있던 것을 읽지 않음으로 (BT24-018 · EX13-015)
  - 생성 한글: 비용 "deleting 1 of your opponent's ~" 앞의 "자신의" 제거, ≪디코이≫ 색 여럿은 "《디코이《레드》/《블랙》》" (공식 BT20-017)
- 카드 데이터: 영어 API 동기화가 이미 받은 세트를 건너뛰어 새 카드 번호를 놓치던 것을 고쳐(전체 번호 목록을 늘 본다) BT27 29장 추가 · BT27-020 종류 수정(디지타마 → 디지몬). 새 카드 29장 중 효과가 있는 25장의 절 87개 중 51개 지원 (7장 전부 동작), 지원 절 중 45개에 생성 한글 (공식 한글 데이터 없음)
- 생성 한글: 새로 지원한 211문장 중 187문장 (공식 표기: "이 카드가 효과로 진화원에서 파기되었을 때," · "이 디지몬의 진화원이 효과로 늘어났을 때," · "자신의 효과로 이 디지몬의 진화원이 늘어났을 때," · "「사역마」(디지몬・옐로・DP 3000・~) 토큰 1마리를 등장시킬 수 있다." · "자신의 시큐리티를 전부 확인한다. 그중 ~ 1장을 패에 추가할 수 있다." · "상대의 디지몬의 진화원을 선택하여 2장 파기한다." 등). 생성 한글이 붙는 절 6,952 → 7,202
- 읽지 않고 남긴 것: 비용 "by deleting 1 of your opponent's ~" · 대상 동작 뒤 "If you did" (LM-018) · 이름에 쉼표가 있는 조건 (EX13-014) · 듀얼 카드의 "Use Requirement: ~" (옵션 면 미구현)
- 테스트 35개 (EffectBatch28Tests)

## v0.27.0 — 2026-10-26
미지원 효과 문장 1차 묶음 — 도감 효과 문장 지원 6,778 → 7,017 / 10,279 (65.9% → 68.3%, +239)
- 모든 절이 동작하는 카드 1,860 → 1,976장, 미지원 절이 남은 카드 2,433 → 2,317장 (수정 전 지원되던 문장 중 깨진 것 0)
- 새 발동 시점 (엔진)
  - 자신의 테이머 등장: "When you play a blue Tamer" · "~ a Tamer with [Tai Kamiya] in its name" · "When any of your Tamers are played" · "When your Digimon or Tamers are played or digivolve" · "When you play [Hina Kurihara]" (패 · 효과 등장 모두)
  - 진화한 카드 조건: "When one of your Digimon digivolves into a red or yellow Digimon" · "~ into a Digimon with [Greymon] in its name" · "~ into a [Puppet] trait Digimon" · "~ into [Diaboromon]"
  - 액티브가 되었을 때 (액티브 페이즈 · 효과 모두, 이미 액티브면 아님 — 공식 Q&A BT2-021 · BT11-069): "When this Digimon becomes unsuspended" · "When a Digimon becomes unsuspended" · "When one of your blue Digimon becomes unsuspended"
  - 자신의 디지몬이 배틀에서 소멸시켰을 때 (어택 · 블록 모두): "When one of your green or blue Digimon deletes an opponent's Digimon in battle (and survives)"
  - 레스트 표기 보강: "any of your opponent's Digimon (or Tamers) suspend" · "a / another Digimon becomes suspended" · "an effect suspends a / another Digimon" · "When any of your [Yao Qinglan] suspend" · 색 · 특징 주어
  - 색 · 텍스트 조건 주어의 등장 ("When any of your white Digimon are played") · "When you play (another) Digimon by an effect" · "When other Digimon are deleted (in battle)" · "When a / another Digimon attacks"
- 동작 · 조건
  - 주어 뒤의 may: "1 of your Digimon may unsuspend" · "This Digimon may unsuspend" · "1 Digimon may gain ＜Security A. -1＞" (해로운 키워드만 줄 때는 상대 디지몬을 먼저 고름)
  - 키워드 + DP: "gains ＜Piercing＞ and +3000 DP" · "gains ＜Raid＞, ＜Piercing＞, ＜Blocker＞ and +3000 DP" · "Give ~ ＜Security A. -1＞ and -3000 DP" · 지속 효과 "All of your [Iliad] trait Digimon gain ＜Alliance＞, ＜Reboot＞ and +2000 DP"
  - "If this effect didn't delete / suspend" (대상이 있으면 반드시 소멸 — 공식 Q&A BT21-068 · P-186, 효과를 받지 않아 소멸하지 않음도 포함 — BT18-082)
  - "Trash the top 2 cards of both players' decks" · "~ of your opponent's deck" (공식 Q&A EX4-008)
  - "Suspend all of your opponent's Digimon and Tamers" · "Suspend 1 of your opponent's Digimon and 1 of their Tamers" · "Delete 1 of your opponent's Tamers" (테이머 대상 규칙이 디지몬 대상 규칙 뒤에 있어 읽히지 않던 것)
  - 이름 조건 "If / While this Digimon is [Alphamon: Ouryuken]" · "~ is [Titamon] or has the [Titan] trait"
  - 생략된 조건 "If you have 3 or more security cards, ~. If you have 3 or fewer, ~"
- 버그 수정: 대상 조건의 "or" — "with [Huckmon] in its name or [Royal Knight] trait"를 둘 다 만족해야 하는 것으로 읽던 것을 둘 중 하나로 (ST12-13 · BT10-092 · BT4-114 · BT13-087 등, 화면 한글도 "~을 포함하거나 특징으로 ~를 가진")
- 생성 한글 (공식 표기): 새 시점 · 동작 239문장 중 223문장 — "블루인 자신의 테이머가 등장했을 때," · "자신의 디지몬이 레드/옐로인 디지몬으로 진화했을 때," · "디지몬이 액티브가 되었을 때," · "~ 자신의 디지몬이 배틀에서 상대 디지몬만을 소멸시켰을 때," · "이 효과로 소멸하지 않았다면," · "서로의 덱 위에서부터 2장 파기한다." · "이 디지몬은 《관통》을 얻고, DP +3000." · "디지몬 1마리에게 《S 어택 -1》을 줄 수 있다" 등. 생성 한글이 붙는 절 6,766 → 6,952
- 읽지 않고 남긴 것: 비용 뒤 "If this effect didn't delete ~" (BT16-081 · BT23-069 — 비용을 내지 않았을 때 뒷문장을 처리하는지 Q&A 없음)
- 테스트 35개 (EffectBatch27Tests)

## v0.26.2 — 2026-10-25
효과 버그 수정: 내장 한글 카드가 영어 효과 문장의 이름 조건에 맞지 않던 문제 · 장한솔(BT1-088) [메인] 조건 미충족
- 내장 카드 120장(ST1~ST6 · BT1~BT5)은 엔진 안의 이름이 한글(예: ST1-07 "그레이몬")이라, 영어 효과 문장의 "[Greymon]" · "with [X] in its name" · "named X" · 같은 명칭 판정에 걸리지 않았다
  - 예: BT4-092 Marcus Damon "When one of your Digimon with [Greymon] in its name attacks"가 BT10-019 Greymon에는 발동하고 ST1-07 그레이몬에는 발동하지 않음
- 내장 카드에 공식 영어 이름(`CardData.EnglishName`, DigimonCard.io 카드 번호별 이름)을 붙이고, 이름 판정을 한 곳(`Models.CardNames`)으로 모아 한글 · 영어 이름을 함께 본다. 화면의 한글 이름은 그대로
  - 이름 포함 · 정확한 이름 · 이름 지정 진화 · "other than [X]" · 같은 명칭(오메가몬 BT1-084 · 크리사리몬 BT2-059 · "same name" 조건 · 같은 명칭 등장 제한)
  - 영향: 내장 카드 이름과 맞는 이름 조건을 가진 API 카드 약 386장 · 문장 약 488개 (Greymon · Omnimon · Garurumon · Agumon · Tai Kamiya 등)
  - "you play another Digimon with the same name as this Digimon"(BT2-053)이 같은 명칭이 아닌 디지몬 등장에도 발동하던 것도 같은 명칭일 때만으로
- 장한솔(BT1-088) 등 테이머 · 디지몬의 [메인] 효과: 조건(그린 Lv.5 이상 등)이 맞지 않으면 레스트하지 않고도 "썼습니다"가 뜨던 것을 "조건 미충족" 실패로 알리고, 이번 턴에 쓴 것으로 치지 않는다 (패 · 트래시 [메인]과 같은 방식)
- 테스트 7개 (NameMatchingTests)

## v0.26.1 — 2026-10-25
효과 버그 수정: "one of your opponent's ~ Digimon" 주어가 자신의 디지몬 시점으로 붙던 문제
- "When one of your opponent's level 5 or lower Digimon is deleted" · "When one of your opponent's Digimon attacks"가 "your ~ Digimon"(자신의 디지몬) 규칙에 먼저 걸려 **자신의 디지몬**이 소멸 · 어택했을 때 발동하고, 상대 디지몬일 때는 발동하지 않았다
  - 예: 디지몬 카이저(BT8-094)가 자신의 Lv.5 이하 디지몬이 소멸해도 레스트하고 드로우 · 상대 디지몬 소멸에는 반응 없음, 사나다 아라타(BT22-091) · BT17-059 · BT23-046 등 [상대의 턴] "상대 디지몬이 어택했을 때" 효과가 한 번도 발동하지 않음, BT7-013 · EX1-064 · EX8-012 등 "상대 디지몬이 소멸했을 때"가 자신의 디지몬 소멸에 발동
- 상대 디지몬이 주어인 "~ is deleted / are deleted / attacks"를 레벨 · 색 · 이름 조건째 상대 디지몬 시점으로 읽는다 (이번에 시점이 바뀐 카드 32장 — 흰수염 도사 BT14-088은 새로 읽힘)
- 테스트 2개 (TriggerVariantTests)

## v0.26.0 — 2026-10-25
화면: 자유 배치 필드 — 레스트 카드(테이머 포함)가 잘 보이게
- 테이머를 이름 칩이 아니라 배틀 에어리어의 카드로 한 장씩 보여 준다 (같은 카드도 묶지 않음). 레스트면 디지몬처럼 가로로 눕힌다. 상대 테이머도 카드로 보인다
- 테이머 카드 클릭 = [메인] 효과 쓰기(쓸 수 있으면 "✦ 클릭: [메인] 효과" 표시) · 아니면 지속 효과 안내, 우클릭 = [메인] 효과, 마우스 오버 = 카드 설명 · 테이머 증가치
- 놓인 ≪딜레이≫ 옵션 · 테이머 아래 카드는 배틀 에어리어 아래 줄에 그대로 (없으면 줄을 숨겨 필드를 넓게)
- 내 배틀 에어리어의 디지몬 · 테이머를 끌어서 원하는 자리로 옮긴다. **놓는 곳으로 구분**: 내 배틀 에어리어 안에 놓으면 자리 이동(효과 없음), 상대 시큐리티 · 상대 레스트 디지몬 위에 놓으면 공격 (테이머는 이동만). 끄는 동안 놓일 자리를 점선 칸으로 미리 보여 준다
- 카드는 필드를 떠날 때까지 자리를 지키고, 진화해도 그 자리에 있다. 다른 카드와 겹치게 놓으면 가장 가까운 빈 자리로 밀린다 (레스트로 넓어진 카드 · 창 크기 변화도 같은 방식)
- 손패 · 육성 디지몬을 배틀 에어리어 빈 곳에 놓으면 놓은 자리에 나온다. 그 밖의 새 카드와 상대(AI) 카드는 빈 자리에 자동 배치 (디지몬은 왼쪽부터, 테이머는 오른쪽 끝부터 · 상대는 거울 배치)
- [▦ 자동 정렬] 버튼: 옮긴 자리를 지우고 격자로 다시 놓는다
- 배틀 에어리어의 고정 4칸 점선 슬롯을 없앴다 (칸 없는 열린 필드 — 격자는 보이지 않는 기준으로만)
- 필드 카드를 끌기 시작할 때 육성 페이즈를 바로 넘기지 않는다 (자리만 옮길 수 있으니, 상대 쪽으로 가져가 공격할 때 넘긴다)
- 위치는 화면에만 있는 정보 (게임 상태 · 저장 파일에 들어가지 않음). 배치 계산은 `DigimonTcg.Core.Layout.FieldLayout` — 테스트 7개 (FieldLayoutTests)

## v0.25.1 — 2026-10-22
생성 한글: 패 · 트래시 [메인]의 진화 · 등장 (v0.25.0, 공식 표기)
- "자신의 트래시의 「슈슈몬」 1장을 자신의 「슈몬」의 진화원 아래에 놓는 것으로, 그 디지몬을 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다."
- "자신의 「아구몬」 1마리를 이 카드로 진화 조건을 무시하고 진화 코스트 6으로 진화시킨다."
- "[트래시]【메인】 ~ 이 카드를 지불하는 등장 코스트 -2 하여 등장시킨다."
- 조건 "If you have [이름]" (a · in play 없는 표기)도 받는다
- 테스트 3개 (KoreanEffectRendererTests)

## v0.25.0 — 2026-10-22
패 · 트래시 [메인] 효과의 동작 · 비용 보강
- "~, it digivolves into this card for a digivolution cost of 3, ignoring digivolution requirements" · "1 of your [Agumon] digivolves into this card ~" (패의 이 카드로 진화 조건을 무시하고 진화)
- "play this card (from your trash) with the play cost reduced by 4" (패 · 트래시의 이 카드를 줄인 코스트로 등장)
- 비용 "placing 1 [ShoeShoemon] from your trash as any of your [Shoemon]'s bottom digivolution card" · "~ from your hand under one of your [Jellymon] as its bottom digivolution card" (놓은 디지몬 = 다음 문장의 "it")
- 자신의 디지몬 조건에 이름만 쓴 표기 ("1 of your [Shoemon]")
- 시나리오 테스트 ZoneAbilityTests에 2개 추가
- 도감 효과 문장 지원 6,764 → 6,777 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.24.0 — 2026-10-22
화면 · AI: 패 · 트래시의 카드로 쓰는 [메인] 효과 (v0.23.0 엔진을 연결)
- 패 카드 우클릭 → 그 카드의 패 [메인] 효과 (쓸 수 있으면 카드에 "우클릭: 이 카드의 패 [메인] 효과 쓰기" 툴팁)
- 트래시 칸 우클릭 → 쓸 수 있는 트래시 [메인] 효과 (여러 장이면 고르기) · 쓸 수 있으면 트래시 장수 옆에 "⚡N"
- 효과 문장(그 곳의 줄)을 보여 주고 쓸지 확인, 조건 · 비용이 맞지 않으면 엔진의 안내
- 개선 AI: 패 · 트래시 효과는 카드 전체가 아니라 그 곳의 효과 줄만 보고 판단, 패 효과는 카드를 내지 못하게 되므로 더 큰 이득일 때만

## v0.23.1 — 2026-10-22
생성 한글: [패]【메인】 「Legend-Arms」 비용 (v0.23.0의 패 · 트래시 [메인])
- "[패]【메인】 1 코스트 지불하고, 이 카드를 특징으로 「Legend-Arms」를 가지거나 Lv.3의 자신의 디지몬의 진화원 아래에 놓는 것으로, ~" — 공식 표기
- "그 디지몬의 DP 이하의 상대의 디지몬 1마리" (with as much or less DP as that Digimon)
- 테스트 1개 (KoreanEffectRendererTests)

## v0.23.0 — 2026-10-22
패 · 트래시의 카드로 쓰는 [메인] 효과 — 엔진 (화면 · AI 판단은 digimontcg-51)
- "[Hand] [Main] ~" · "[Trash] [Main] ~"를 읽음 (EffectTrigger.HandMain · TrashMain)
- 엔진: GetHandMainAbilities · GetTrashMainAbilities(owner) — 필드 [메인]과 같은 MainAbility(Zone = Hand / Trash), ActivateHandEffect · ActivateTrashEffect(card) — 자기 메인 페이즈에, 같은 카드 1장마다 턴에 1번, 조건 · 비용이 맞지 않으면 Fail(이유)
- AI: 필드 [메인]과 함께 패 · 트래시 효과도 AiPolicy.ShouldUseMainAbility로 판단
- 비용 "paying N cost and placing this card as the bottom digivolution card of 1 of your Digimon (that's level 3 or has the [X] trait)" — 놓은 디지몬이 다음 문장의 "that Digimon", "the Digimon this was placed under" 표기
- 시나리오 테스트 ZoneAbilityTests 5개, AI 벤치마크 그대로 (56.5% / 67.6%)
- 도감 효과 문장 지원 6,758 → 6,764 / 10,279 (화면 연결 뒤 플레이어가 쓸 수 있음, 수정 전 지원되던 문장 중 깨진 것 0)

## v0.22.0 — 2026-10-22
효과로 옵션을 코스트 없이 사용
- "You may use 1 Option card with a cost of 5 or less in your hand without paying the cost" · "~ from your hand or under your Tamers ~" · "~ from your hand or trash ~" · "1 use cost 3 or lower Option card with [X] in its text" · "single-color Option card"
- 코스트만 내지 않고 색 조건은 그대로 확인 — 사용할 수 있는 옵션만 고름. [메인] 효과 → 트래시 · 링크 등 사용 뒤 처리 → "옵션을 사용했을 때"까지 보통 사용과 같음
- 카드 조건: "use cost" 표기, "single-color"(1색)
- 시나리오 테스트 OptionUseByEffectTests 3개
- 도감 효과 문장 지원 6,742 → 6,758 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.21.1 — 2026-10-22
생성 한글: 옵션을 코스트 없이 사용 (공식 표기)
- "자신의 패에서 사용 코스트 5 이하의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다."
- 색 · 단색: "퍼플인 사용 코스트 6 이하의 옵션 카드" · "특징으로 「CS」를 가진 1색의 옵션 카드"
- 장소: "자신의 패 또는 테이머 아래에서" · "자신의 패 또는 트래시에서"
- 실제 카드 28장에서 29곳이 한글로 (남은 것: "디지몬 또는 옵션 카드를 등장 · 사용" 복합 1문장)
- 테스트 6개 (KoreanEffectRendererTests)

## v0.21.0 — 2026-10-22
AI가 강화 옵션을 쓴다 · 선택형 코스트 감소를 AI 판단과 화면 안내에
- 개선 AI는 옵션을 "핵심 위협 제거"일 때만 써서, 추천 덱 720판에서 옵션을 한 번도 쓰지 않았다
  (코스트 초과 697번 · 강화 옵션이라 제외 285번 · 비용 있는 제거 56번)
- "이 턴 동안 자신의 디지몬 1마리의 DP +N" 옵션: 어택 전 · 지금 메모리 안에서, 그 버프로 어택 점수가 크게 오를 때만
  (블로커에게 막혀 질 어택을 이기게 하는 등) 그 디지몬에게 쓴다 → 66.7% → 67.6% (옵션 79번), 기존 AI 56.5% 그대로
- 개선 AI가 낼 디지몬을 고를 때 "비용을 내면 -N" 선택형 감소까지 계산 (v0.20.8의 PreviewPlayCostWithOptions)
- 화면: 디지몬을 끌 때 등장 코스트를 "5, 비용을 내면 1"처럼 안내 (실제로 낼지는 등장할 때 묻는다)

## v0.20.8 — 2026-10-22
AI용 등장 코스트 미리보기 (선택형 감소 포함)
- GoldfishEngine.PreviewPlayCostWithOptions(owner, card): 상대 필드를 포함한 코스트에서, 지금 낼 수 있고 추천이 참인 "비용을 내면 -N" 감소를 뺀 값 (부작용 없음 — AI가 실제로 낼 때와 같은 기준)
- PlayCostOption.CostText: 원문 비용 구절 ("deleting 1 of your Digimon" 등) — AI가 비용 종류를 가늠
- 게임 동작은 그대로 (AI 정책 반영은 다음 버전)

## v0.20.7 — 2026-10-22
덱 위 공개: 같은 카드 2장을 모두 고를 수 있게
- 같은 카드 여러 장은 같은 카드 객체라, "Among them, add 1 [X] to the hand and trash 1 such card"처럼 두 번 고를 때 앞에서 1장을 가져가면 남은 같은 카드까지 고를 수 없었음 → 공개한 순서의 번호로 기억해서 고름
- 시나리오 테스트 RevealVariantTests에 1개 추가, AI 벤치마크 기준 그대로 (56.5% / 66.7%)

## v0.20.6 — 2026-10-22
대량 무작위 대전에서 찾은 링크 버그 2건 수정
- 시큐리티에서 나온 옵션이 "이 카드를 링크한다"로 링크 카드가 되면 트래시에도 남아 카드가 1장 늘었다 (예: BT24-092)
- 패에서 쓴 옵션이 링크되면 같은 카드의 다른 사본이 패에서 함께 사라졌다 (예: BT24-095 · ST22-09 · BT25-100 — 같은 카드 여러 장이 같은 데이터를 공유)
- "효과가 일어나지 않았습니다" 로그는 카드 본체 효과만 — 진화원 효과는 어택마다 여러 장이 함께 발동해 로그가 묻혔다
- 대량 무작위 대전 (카드 풀 4,072종을 색별로 돌아가며 덱에 넣음, 플레이어는 등장 · 진화 · 옵션 · 기동 효과 · DNA · 링크 · 어택을 시도)
  - 1회차 24,000게임: 카드 수 위반으로 위 버그 발견
  - 2회차 32,000게임 (수정 후): 예외 0 · 카드 수 위반 0 · 멈춤 0, 4,070종 실제 사용 (못 낸 2장은 코스트 13 · 16)
- 시나리오 테스트 SecurityLinkOptionTests 2개

## v0.20.5 — 2026-10-22
효과가 대상 · 조건이 없어 아무것도 안 하면 로그에 남김
- [등장 시] · [진화 시] · [어택 시] · [소멸 시] 효과가 대상 · 조건이 없어 끝나면 "[진화 시] 로제몬: 대상 · 조건에 맞는 것이 없어 효과가 일어나지 않았습니다"
- 조용히 넘어가면 "효과가 안 탔다"로 보였다 (사용자 신고, 여러 덱 시뮬레이션에서 로제몬 · 쥬드몬 · 엔젤우몬 등 내장 스크립트)
- 여러 덱 시뮬레이션: 39덱(추천 6 · 단색 테마 12 · 2색 15 · 효과 위주 6) × 20게임 = 780게임 — 예외 0 · 카드 수 보존 위반 0 · 30턴 초과 0 · 옵션 대상 조건 거절 정상
- 시나리오 테스트 NoEffectLogTests 2개

## v0.20.4 — 2026-10-22
화면: 대상으로 고를 디지몬이 없는 옵션도 쓸 수 있게 (공식 룰 — 대상이 없어도 옵션은 쓸 수 있다)
- v0.20.2 · v0.20.3 뒤로 대상 옵션 123장은 대상 디지몬 위에 놓아야만 쓰여, 조건에 맞는 디지몬이 없으면 쓸 수 없었다
  (예: "상대 디지몬 1마리를 소멸시킨다. 그 뒤 ≪1 드로우≫"를 드로우하려고 쓰는 경우)
- 대상 쪽에 조건에 맞는 디지몬이 하나도 없으면 필드 어디에 놓아도 대상 없이 쓴다 ("대상 없이 씁니다" 안내)
- 자신의 디지몬 대상 옵션도 대상 조건에 맞는 디지몬만 강조 · 빈 칸에 놓으면 조건에 맞는 디지몬 중 DP가 가장 높은 것

## v0.20.3 — 2026-10-22
문장으로 만든 옵션 카드에도 대상 조건 (v0.20.1 · v0.20.2의 이어서)
- 옵션 [메인]의 첫 대상 "1 of your (opponent's) ~ Digimon"으로 대상 종류(자신 · 상대)와 조건(레스트 · 레벨 · DP · 특징 등)을 채움 — 도감 옵션 519장 중 123장에 대상 종류, 40장에 대상 조건
- 조건에 안 맞는 디지몬에 놓으면 메모리를 쓰기 전에 "[카드]은(는) 레스트 상태인 상대의 디지몬에게만 쓸 수 있습니다" 안내로 막음 (화면 강조도 같은 조건)
- 대상이 전부 · 여러 마리 · 양쪽이면 예전처럼 엔진이 고름
- 시나리오 테스트 OptionMainTargetTests 4개, AI 벤치마크 기준 그대로 (56.5% / 66.7%)

## v0.20.2 — 2026-10-22
화면: 옵션의 대상 조건을 끌기 · 놓기 · 클릭 고르기에도 적용 (v0.20.1 엔진 수정의 화면 쪽)
- 상대 디지몬을 고르는 옵션을 고를 때 대상 조건(예: 전기 충격 = 레스트 상태)에 맞는 디지몬만 강조 · "적중!" 버튼 · 클릭 대상
- 끄는 중 조건에 안 맞는 디지몬 위에서는 "쓸 수 없습니다 (대상 조건)" 안내
- 디지몬이 아닌 칸에 놓으면 DP가 가장 높은 상대 디지몬이 아니라 조건에 맞는 디지몬 중 DP가 가장 높은 것, 없으면 쓰지 않고 안내

## v0.20.1 — 2026-10-22
옵션 [메인]의 대상 조건 (사용자 신고: "전기 충격이 레스트되지 않은 디지몬에도 적용된다")
- 전기 충격을 액티브 디지몬에 놓으면 엔진이 그 대상을 버리고 다른 레스트 디지몬을 말없이 되돌리거나, 효과 없이 메모리만 쓰였다
- 이제 조건에 안 맞는 디지몬에 놓으면 메모리를 쓰기 전에 막고 안내 ("[전기 충격]은(는) 레스트 상태인 상대 디지몬에게만 쓸 수 있습니다")
- 내장 옵션 4장에 조건 등록: 전기 충격(레스트) · 소로 블루(진화원 없음) · 제노사이드 어택(등장 코스트 7 이하) · 데스 클로(Lv.4 이하)
- CardScript.MainTargetFilter / MainTargetRule, CardScripts.IsValidMainTarget (화면 강조용) · InvalidMainTarget
- 시나리오 테스트 OptionTargetFilterTests 3개

## v0.20.0 — 2026-10-22
경기 로그를 파일로 자동 저장 (사용자 신고 "20:10 경기 로그를 확인해 달라" — 로그가 메모리에만 있어 앱을 끄면 사라졌다)
- `%LOCALAPPDATA%\DigimonTcg\logs\game_yyyyMMdd_HHmmss.txt` (경기 시작 시각) — 로그가 늘 때마다 덮어쓰기, 최근 30개만 남김
- 첫 줄: 앱 버전 · 스테이지 · 내 덱 · 상대 덱. 앱을 켜기만 한 배경 대련 준비 단계는 남기지 않는다
- 파일을 못 써도 게임은 그대로 진행

## v0.19.2 — 2026-10-22
비용을 내지 못하거나 내지 않은 효과도 로그에 남김 (사용자 신고: "팔몬 등장 시 효과가 안 탄다")
- ST21-07 팔몬 "By trashing 1 card with the [ADVENTURE] trait in your hand, ＜Draw 2＞"처럼 비용이 붙은 효과는, 패에 버릴 카드가 없으면 묻지도 로그도 없이 넘어가 효과가 안 나온 것처럼 보였다
- 이제 "[팔몬] 비용을 낼 수 없어 효과를 발휘하지 않습니다 (…)" · 거절하면 "[팔몬] 효과를 발휘하지 않았습니다 (…)"를 남김 (SentenceParser의 "By ~ing, ~" 두 곳)
- 시나리오 테스트 CostSkipLogTests 3개

## v0.19.1 — 2026-10-22
생성 한글: v0.19.0 발동 시점 묶음 (공식 표기)
- "자신의 효과로 자신의 패를 파기했을 때," · "상대의 패가 파기되었을 때," · "자신의 덱이 파기되었을 때," · "효과로 상대의 패에 카드가 추가되었을 때,"
- "자신이 사용 코스트 2 이상의 옵션 카드를 사용했을 때," · "자신이 특징으로 「X」를 가진 옵션 카드를 사용했을 때,"
- "효과로 자신의 / 상대의 디지몬(또는 테이머)이 등장했을 때," · "자신의 / 이 / 다른 디지몬(또는 테이머)이 효과로 레스트했을 때," · "디지몬이 레스트했을 때,"
- 테스트 13개 (KoreanEffectRendererTests)

## v0.19.0 — 2026-10-22
발동 시점 묶음 — 옵션 사용 조건 · 패/덱 파기 · 효과로 등장/레스트 · 상대 패
- "When you use an Option card with a cost of 3 or more" · "When you use [TS] trait Option cards" (사용한 옵션의 조건, "use cost" · "memory cost" 표기 포함)
- "When your hand is trashed from" · "When one of your effects trashes a card in your hand" · "When your opponent's hand is trashed from" (패를 버린 뒤 1번)
- "When a card is trashed from your deck" (덱 위 파기 1번에 1회)
- "When an effect adds cards to your opponent's hand" (패로 되돌리기)
- "When an effect plays one of your / an opponent's Digimon" (코스트 없이 효과로 등장했을 때만)
- "When an effect suspends one of your Digimon / this Digimon" (어택 · 블록으로 레스트한 것은 빼고), "When any Digimon suspend" (양쪽)
- 시나리오 테스트 TriggerVariantTests 9개, KeywordTests의 "미지원 문장" 예시 교체, AI 벤치마크 기준 그대로 (56.5% / 66.7%)
- 도감 효과 문장 지원 6,669 → 6,742 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.18.1 — 2026-10-22
생성 한글: 진화 코스트 감소 (v0.18.0) 등
- "이 디지몬이 특징으로 「로얄 나이츠」를 가진 디지몬 카드로 진화할 때, 지불하는 진화 코스트를 -1 한다." — 공식 표기
- 진화하는 쪽(이 디지몬 · 자신의 디지몬 · 특징 「X」의 자신의 디지몬), "패의 이 카드로", 셈("그 디지몬의 뒷면의 진화원 1장마다" · "자신의 그린/블랙의 테이머 1장마다")
- 비용 2개: "이 테이머를 레스트시키고, 자신의 테이머 아래의 카드 1장을 그 디지몬의 진화원 아래에 놓는 것으로,"
- "~ in its text"는 공식대로 "「X」이 기술되어 있는"
- 트래시 장수 조건 "자신의 트래시에 명칭에 「묘티스몬」을 포함하는 카드가 5장 이상 있을 때," · 비용 "시큐리티가 가장 많은 플레이어 1명의 시큐리티를 위에서부터 1장 파기하는 것으로,"
- 진화 코스트 감소가 있는 실제 카드 95장 중 이 문장 때문에 원문으로 남는 카드 3장
- 테스트 1개 (KoreanEffectRendererTests)
- 진화 · 등장 코스트 미리보기에 코스트 감소 반영 (화면 · 개선 AI, 8cbd4c5 — 51)

## v0.18.0 — 2026-10-22
진화하기 직전의 진화 코스트 감소
- "When this Digimon would digivolve (into a [X] trait Digimon card), reduce the digivolution cost by N" · "When one of your (suspended / [X] trait) Digimon would digivolve into ~" · "When one of your Digimon with ~ would digivolve into this card in your hand, reduce the memory cost of the digivolution by N" (패의 그 카드)
- [Your Turn] · [Once Per Turn](효과가 있는 곳마다), 조건 "if you have ~", 비용 "by ~ing, reduce ~"(낼지 묻는다 — AI는 추천대로)
- 효과가 있는 곳: 자신의 디지몬(맨 위 카드 · 진화원 효과) · 테이머 · 진화할 카드 자신. 상대가 진화 코스트 감소를 막고 있으면 적용하지 않음
- 플레이어 진화 · AI 진화 · 효과로 진화(코스트를 낼 때) 모두에 적용, GoldfishEngine.PreviewEvoCost(owner, target, card) — 비용 없는 감소만 넣은 미리보기
- 카드 조건: "with ＜Save＞ in its text" · "multicolored (purple or yellow) Digimon", 대상 조건 "with a Tamer card in its digivolution cards", 원문 오타 "trait trait"
- 조건 "you have 4 or more cards with [X] in its text in your trash", 비용 "trashing the top security card of 1 player with the most security cards"
- 시나리오 테스트 EvoCostModifierTests 6개, AI 벤치마크 기준 그대로 (56.5% / 66.7%)
- 도감 효과 문장 지원 6,579 → 6,669 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.17.1 — 2026-10-22
생성 한글: v0.17.0의 새 비용 표기
- "자신의 시큐리티의 위 또는 아래에서 1장 파기하는 것으로,"
- "자신의 패에서 / 트래시에서 / 덱 위에서 카드 1장을 이 디지몬의 진화원 가장 아래에 뒷면으로 놓는 것으로,"
- "자신의 테이머 아래의 뒷면 카드를 아래에서부터 1장 (또는 자신의 시큐리티를 위에서부터 1장) 파기하는 것으로,"
- "your opponent's Digimon effects don't affect ~" (소유격 없는 표기)도 "상대의 디지몬의 효과를 받지 않는다"로
- 이 비용을 가진 실제 카드 54장 중 비용 때문에 원문으로 남는 카드 0장
- 테스트 1개 (KoreanEffectRendererTests)

## v0.17.0 — 2026-10-22
비용("By ~ing, ~") 표기 확장
- "By suspending 2 Digimon" · "By suspending 1 of your [WG] trait Digimon"(자신의 디지몬, 이 디지몬도 가능) · "By suspending 1 of your yellow Tamers"
- "By unsuspending this Digimon"
- "By trashing your top or bottom security card" · "~ the top or bottom card of your security stack" (위 · 아래 중 고름)
- "By placing 1 card in your hand (face down) as this Digimon's bottom digivolution card" · "~ from your trash ~" · "By placing your deck's top card face down as ~"
- "By trashing the bottom face-down card from under any of your Tamers" (테이머 아래 카드 — 마지막에 놓인 것이 맨 아래)
- 시나리오 테스트 CostVariantTests 4개, AI 벤치마크 기준 그대로 (56.5% / 66.7%)
- 도감 효과 문장 지원 6,516 → 6,579 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.16.1 — 2026-10-22
생성 한글: 코스트 감소를 공식 표기로 (v0.16.0의 비용을 내고 줄이기 포함)
- "패의 이 카드를 등장시킬 때, 자신의 「디아블로몬」 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -12 한다." — "by ~ing, reduce" · "you may ~ to reduce"
- 셈 "상대의 디지몬 1마리마다 지불하는 등장 코스트를 -2 한다." · 조건 "블루인 자신의 디지몬이 있다면 지불하는 등장 코스트를 -1 한다."
- 옵션은 "이 카드를 사용할 때, ~ 지불하는 사용 코스트를 -N 한다." (전에는 등장 · 사용을 한 문장으로 썼음)
- 테스트 1개 추가 · 1개 표기 수정 (KoreanEffectRendererTests)

## v0.16.0 — 2026-10-22
비용을 내고 등장 코스트를 줄이는 효과
- "When this card would be played, by deleting 1 of your Digimon, reduce the play cost by 4." · "~ you may delete 1 of your [Diaboromon] to reduce this card's play cost by 12."
- 등장할 때(패를 떠난 뒤 · 필드에 놓기 전) 비용을 낼지 묻고, 내면 코스트를 줄임 — 플레이어는 묻고, AI는 추천대로
- 비용은 기존 "By ~ing, ~" 비용 읽기(ParseCost)를 그대로 씀 (패 버리기 · 자신의 디지몬 소멸 · 트래시에서 되돌리기 등)
- AI 등장도 코스트를 필드에 놓기 전에 정함 (플레이어와 같은 순서 — 이 카드가 비용 · 조건에 들어가지 않게)
- 시나리오 테스트 PlayCostOptionTests 4개, AI 벤치마크 기준 그대로 (56.5% / 66.7%)
- 도감 효과 문장 지원 6,503 → 6,516 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.15.1 — 2026-10-22
생성 한글: 어택 대상 변경 (v0.15.0의 새 시점 · 동작)
- "어택의 대상이 변경되었을 때," · "이 디지몬의 어택의 대상이 변경되었을 때," · "자신의 디지몬의 어택의 대상이 변경되었을 때,"
- "어택의 대상을 ~로 변경할 수 있다" — switch 표기, 이름만 있는 대상("자신의 「에오스몬」 1마리")
- 테스트 1개 (KoreanEffectRendererTests)

## v0.15.0 — 2026-10-22
어택 대상 변경 — 발동 시점 · 동작 · 잠금
- 새 발동 시점: "When an attack target is switched" · "When attack targets change"(어느 쪽이든), "When this Digimon's attack target is switched", "When one of your Digimon's attack targets is switched" — 효과(RedirectAttack)나 ≪돌진≫으로 대상이 바뀐 뒤
- "Change / Switch the attack target to 1 of your (suspended / [X] trait) Digimon" (이 디지몬으로만 되던 것을 넓힘)
- "This Digimon's attack target can't change / can't be switched": 이 어택 동안(효과) · 지속 효과([Your Turn] 등) — 효과로도 ≪돌진≫으로도 바뀌지 않음
- 시나리오 테스트 AttackTargetSwitchTests 3개, AI 벤치마크 기준 그대로 (56.5% / 66.7%)
- 도감 효과 문장 지원 6,457 → 6,503 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.14.2 — 2026-10-22
화면 한글 정리 (실제 앱 화면 점검)
- 화면 표시(51): 카드 상세 머리말 · 덱 목록 · 카드 배지 · 덱 빌더 COLOR / TYPE 필터의 색 · 종류를 한글로 (레드 디지몬 · 테이머 · 옵션 · 디지타마), 효과 지원 상자는 동작하지 않는 절을 생성 한글로 보여 주고 영어 원문 · 사유는 툴팁으로, 덱 빌더 툴팁이 대전 화면에 남던 문제, 창이 작업 영역보다 커서 아래가 작업 표시줄에 가리던 문제 (시작할 때 작업 영역 가운데에 맞춤)
- 개선 AI의 등장 코스트 계산에 상대 필드를 넘김 (51)
- 생성 한글: 조건 없는 《디코드》 · 《파티션》 등, "다음 상대의 액티브 페이즈에 액티브가 되지 않는다", 뒤에 오는 조건("reduce ~ if ~"), "상대의 (액티브 상태인) 디지몬이 없는 동안", "이 디지몬이 2색 이상인 동안", "다른 디지몬이 등장하거나 진화했을 때, 그중 특징으로 ~", "다른 「X」를 포함하는 디지몬을 소멸시키는 것으로", 패 또는 트래시에서 진화
- 테스트 1개 (KoreanEffectRendererTests)

## v0.14.1 — 2026-10-22
카드 한글 표시 — 실제 앱 화면 점검으로 찾은 것
- 로그 머리말: AI 턴은 "턴 종료" 대신 "상대 턴", 턴을 시작하는 줄은 "턴 시작"
- 공식 한글이 없는 카드의 특징도 한글로 (예: Shaman · Iliad · Vaccine → 신인형 · 일리아스 · 백신종 — 디지크로스 · 링크 조건 판정도 한글 특징으로)
- 생성 한글: 앞에 오는 기간("Until your opponent's turn ends, ~"), "이 디지몬이 「X」이거나 특징으로 「Y」를 가지는 동안", 서로 드로우, 어택 대상 변경 불가 · 변경, 효과로 하는 어택("~로 어택할 수 있다", 진화원 N장 이상 조건 포함), 상대의 효과를 받지 않음, 배틀하지 않고 등장, 이름 주어 시점("자신의 「X」가 등장했을 때 / 레스트했을 때"), 패 · 시큐리티에서 파기되었을 때, 《딜레이》 시점 문장
- 공식 한글이 없는 카드 중 효과가 모두 한글인 카드 269 → 287장
- 테스트 2개 (KoreanEffectRendererTests)

## v0.14.0 — 2026-10-22
등장 코스트 감소 — 상대 필드 조건 · 셈 · 여러 표기
- 코스트 감소가 상대 필드도 봄: "if your opponent has 2 or more Digimon" · "for each of your opponent's Digimon" (CardScripts.EffectivePlayCost에 상대 필드 인수)
- "~ reduce the play cost by 2 for each / for every ~" (셈만큼), 뒤에 붙는 조건 "~ reduce its play cost by 1 if you have a blue Digimon in play"
- "When this card would be played from the hand" · "When you would play this Digimon" · "reduce this card's play cost"
- 조건 표기(코스트 감소 · "If ~" · "While ~" 공용): "you have [A] or [B]" · "you don't have another [X] in play", "there is a Digimon with 13000 DP or more" · "there are 2 or more suspended Digimon"(양쪽), "there are N or more levels' total worth of Digimon", "you have fewer Digimon than your opponent" · "your hand has fewer cards than your opponent's"
- 시나리오 테스트 PlayCostReductionTests 9개, AI 벤치마크 기준 그대로 (56.5% / 66.7%)
- 도감 효과 문장 지원 6,406 → 6,457 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.13.0 — 2026-10-22
효과로 하는 어택 — "~ may attack"
- "This Digimon may attack" · "1 of your Digimon may attack" · "It / That Digimon may attack" (자기 턴에만, 등장한 턴 · 어택 불가 효과 확인)
- 대상 제한: "~ may attack a player"(시큐리티만) · "~ may attack your opponent's Digimon"(레스트 상대 디지몬만)
- "~ may attack without suspending" · "you may attack with this Digimon without suspending it" (레스트하지 않고 어택, 이미 레스트여도 가능)
- 플레이어: 어택할지 · 대상을 고르고 곧바로 어택. AI: 정책(ChooseEffectAttack)이 고르고 AI 어택 큐에 넣음 ([자신의 턴 종료 시] 효과의 어택도 처리)
- 시나리오 테스트 EffectAttackTests 6개 (AI 턴 종료 시 어택 포함), AI 벤치마크 기준 그대로 (56.5% / 66.7%)
- 도감 효과 문장 지원 6,359 → 6,406 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.12.1 — 2026-10-22
카드 한글 표시 점검 2회차 (공식 한글이 없는 카드에 쓰는 생성 한글)
- 7단계 문장 한글: "배틀 에어리어를 벗어날 때 / 소멸할 때"(자신의 효과 이외 · 배틀 이외 · 상대의 효과로), "벗어나지 않는다", 링크(링크했을 때 · 링크한다 · 링크 카드 파기 비용), 조그레스 진화(공식 표기 "이 디지몬과 다른 자신의 디지몬으로, 패의 ~로 조그레스 진화할 수 있다"), 육성 에어리어 이동 · 부화, 앞면 시큐리티(앞면으로 놓기 · 뒤집기 · 앞면 시큐리티 체크), "레스트할 수 없다" · "액티브가 되지 않는다", 주인 없는 대상("디지몬 1마리를 레스트시킨다")
- 생성 한글에서 "이 효과로 등장한 카드의 【등장 시】 효과는 발휘하지 않는다"가 빠지던 문제 (BT2-038 등)
- 선택지 고르기 창(ChooseOption)의 질문 · 선택지 카드 이름도 공식 한글 이름으로
- 엔진이 지원하는 효과 문장의 한글 생성 90.2% → 93.7%, 공식 한글이 없는 카드 중 효과가 모두 한글인 카드 234 → 269장
- 테스트 1개 (KoreanEffectRendererTests)

## v0.12.0 — 2026-10-22
효과로 진화할 때 코스트 감소 · 주어가 뒤에 오는 진화 문장
- "~ in your hand for its digivolution cost. When it would digivolve by this effect, reduce the cost by N." (앞 문장의 진화에 코스트 −N을 붙임, "When digivolving with this effect, ~" · 원문 오타 "yellowuce" 포함)
- "You may digivolve this Digimon / 1 of your other Digimon into ~" → 주어를 앞으로 옮겨 읽음, 진화할 디지몬에 "other" · 특징 같은 조건 허용
- 이 문장들이 읽히면서 "・" 고르기 효과(＜딜레이＞ 옵션 등)도 함께 동작
- 시나리오 테스트 DigivolveCostReductionTests 2개
- 도감 효과 문장 지원 6,290 → 6,359 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.11.1 — 2026-10-22
카드 한글 표시 점검 · 수정 (공식 한글 데이터가 있는 PC 기준 — 없으면 예전처럼 영어)
- 로그 · 상대 턴 리플레이 · 알림(토스트) · 시큐리티 결과 · 확인 질문의 카드 이름을 공식 한글 이름으로 (예: [Chapmon] → [챠프몬], [MetalGarurumon] → [메탈가루몬]). 엔진 판정은 영어 이름 그대로
- 로그 머리말의 단계 이름 한글화 (Breeding → 육성, TurnEnd → 턴 종료 …)
- 공식 한글이 없는 카드도 같은 영어 이름의 공식 한글 이름으로 표시 (영어 이름 683장 → 225장)
- 공식 한글 데이터에 【시큐리티】 효과가 빠진 BT11 옵션 17장: 영어 문장으로 만든 한글로 채움
- 이름표 · 특징표 오류: WereGarurumon → 워가루몬 ACE(×) → 워가루몬, Hybrid → 배리어블종(×) → 하이브리드체, Variable → 배리어블종, Appmon → 어플몬
- 생성 한글 뜻 오류: "《시큐리티 어택》을 가진 상대 디지몬 1마리마다"가 빠지던 문장(BT12-044), "진화시킬 수 있다"가 "진화시킨다"로 나오던 문장(BT14-007), 특징 이름이 영어로 남던 조건
- 7단계 키워드 한글: 《링크 +N》 · 〈링크〉 조건 · 《프래그먼트《N》》 · 《디코드《조건》》 · 《파티션《A&B》》 · 《오버클럭《특징》》 · 《디코이《조건》》 · 《디태치》 · 《사용 조건》 · 《인게이지》 · 《어센션》 · 《가드》
- 테스트 3개 (KoreanEffectRendererTests)

## v0.11.0 — 2026-10-22
주인을 말하지 않은 대상 · 레스트 / 액티브 불가
- "suspend 1 Digimon" · "up to 2 Digimon"처럼 주인이 없는 대상 = 양쪽 디지몬 중에서 고름 (레스트 · 소멸 · DP − 같은 해로운 효과는 상대 디지몬을, 액티브 · DP + · 키워드 부여는 자신의 디지몬을 먼저 추천)
- 조건 "If this effect suspended your Digimon" · "If this effect suspended your opponent's Digimon" · "If this effect unsuspended"
- 새 규칙: "~ can't unsuspend until ~" (액티브 페이즈 · 효과로 액티브가 되지 않음), "~ can't suspend until ~" (어택 · 블록 · 효과로 레스트가 되지 않음)
- "can't unsuspend in their next unsuspend phase" 표기
- 시나리오 테스트 SuspendLockTests 10개
- 도감 효과 문장 지원 6,198 → 6,290 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.10.0 — 2026-10-22
"덱 위 N장 공개" 효과의 여러 표기와 공개한 카드에 하는 일
- 공개 앞에 붙는 비용 · 조건: "By suspending this Digimon, reveal ~" · "If it's your opponent's turn, reveal ~" · "suspend this Tamer to reveal ~"
- 공개한 카드로 여러 가지: 패에 넣기 + 파기 · 패에 넣기 + 등장 · 진화원 아래에 놓기(이 디지몬 · 테이머 아래 · 고른 디지몬) · 패에 넣거나 진화원 아래에 놓기
- "1 such Tamer card"(앞 카드와 같은 조건), "2-color black card" · "card with 2 or more colors", "―both with the [X] trait―"
- 나머지 처리: "to the top of the deck"(덱 위로) · "Return the revealed cards ~" · "Trash the revealed cards"
- 덱 위 1장 공개: "If that card has the [X] trait" · "Trash the rest" · "Reveal 1 card from the top of your deck"
- 공개 뒤 조건 "If you added cards" · "If this effect added to a hand" · "If a card is placed by this effect"
- 시나리오 테스트 RevealVariantTests 11개
- 도감 효과 문장 지원 6,130 → 6,198 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.9.0 — 2026-10-22
새 발동 시점 — 시큐리티에서 카드가 빠졌을 때 / 놓였을 때
- "When your security stack is removed from" · "When a card is removed from your (opponent's) security stack" · "When security stacks are removed from" · "When a card is added to your security stack"
- 엔진에서 시큐리티가 빠지는 11곳(체크 · 효과로 파기 · 패에 추가 · ≪방벽≫ 등)과 놓이는 곳(≪리커버리≫ · 앞면으로 놓기 · 디지몬을 시큐리티로)에서 알림
- 한글 생성 문구 추가, 시나리오 테스트 SecurityEventTests 6개
- 도감 효과 문장 지원 6,066 → 6,130 / 10,279 (수정 전 지원되던 문장 중 깨진 것 0)

## v0.8.3 — 2026-10-21
- 효과 문장 표기 점검: 같은 뜻인데 표기만 달라 못 읽던 문장 84개를 읽게 함 (도감 효과 문장 5,982 → 6,066 / 10,279, 기존에 되던 문장 중 깨진 것 0)
  - 손패 · 트래시 · 테이머 · 디지몬 장수, "상대에게 액티브 디지몬이 없음", 양쪽 시큐리티 합계 같은 조건을 "If ~"와 "While ~" 양쪽에서 같이 읽음
  - "this Digimon has N DP or more", "N or more colors", "it's your opponent's turn", 관사 없는 "If you have [X] in play"
  - 어순: 앞에 붙는 "Until your opponent's turn ends, ~" · "To 1 of your opponent's Digimon, give -N DP ~"
  - 원문 오타 "batttle", 줄임말 "there're"
- 시나리오 테스트 WordingVariantTests 11개

## v0.8.2 — 2026-10-21
- ≪리커버리≫ 확인 · 보강: 내장 카드(엔젤우몬 · 홀리엔젤몬 · 홀리드라몬)와 실제 도감 카드 문장으로 시나리오 테스트(RecoveryTests 15개)
- 문장 파서: "N or fewer cards in your security stack" · "N security cards or fewer" 조건 표기, 원문 오타 "3 of fewer", "≪리커버리≫ until there are N cards in your security stack"(N장이 될 때까지 반복) 인식 → 리커버리 문장 지원 48 → 52 / 112

## v0.8.1 — 2026-10-21
- 앱 버전 관리 추가: 버전 번호 한 곳 관리, 빌드한 커밋 해시 자동 표시(로비 · 창 제목 · 오류 로그), 이 변경 기록과 버전 태그

## v0.8.0 — 2026-10-21 (`bf07dad`)
효과 엔진 7단계 — 특수 메커닉
- 링크 · DNA 진화(조그레스) · 디지크로스 · 육성/디지타마 · 앞면 시큐리티 · ≪볼텍스≫ · ≪에그제큐트≫
- ≪디코드≫ · ≪파티션≫ · ≪프래그먼트≫ · ≪디코이≫ · ≪가드≫ · ≪트레이닝≫, "배틀 에어리어를 떠나려 할 때 / 소멸하려 할 때" 효과
- 화면: 링크 카드 표시 · 패 카드를 디지몬에 놓아 링크, DNA 진화 화면, 앞면 시큐리티 표시, 육성 에어리어 우클릭 기동 효과
- 개선 AI: DNA 진화 · 턴 종료 어택 판단
- 수정: 카드 고르기 창(후보 1장일 때도 표시, "~할 수 있다" · "up to"는 고르지 않아도 됨), 시큐리티 5장 초과 표시

## v0.7.0 — 2026-10-21 (`28c0401`)
특수 키워드 · 개선 AI · 공식 Q&A
- 효과 엔진 7단계 1차: ≪딜레이≫ · 【카운터】 ≪블래스트 진화≫ · ≪세이브≫ · 테이머 아래 카드 · ≪머티리얼 세이브≫ 등
- 여러 장을 한 번에 고르는 선택 창
- AI 정책 분리 (기존 AI · 한 수 앞 평가 개선 AI), 개선 AI 승률 56.5% → 66.8%
- 공식 카드 Q&A 62건(내장 120장)을 엔진 시나리오 테스트로 — 불일치 2건 수정
- 수정: 코인 토스 뒤 멀리건 화면이 뜨지 않아 실전 대련이 멈추던 문제, 엔진 점검 지적 사항

## v0.6.0 — 2026-10-20 (`17fc85b`)
- 효과 엔진 6단계: 엔진이 해석한 효과를 한글 문장으로 생성 (공식 한글이 없는 카드용)

## v0.5.0 — 2026-10-20 (`1d1d286`)
공식 한글 데이터 · 효과 지원률 90%
- 효과 엔진 5단계: 우선 세트(ST1~ST6 · BT1~BT5) 효과 지원률 90%, [메인] 기동 효과, ≪디지버스트≫
- 한국 공식 사이트 수집기 → 도감 · 카드 상세 · 대전 화면에 공식 한글 이름 · 효과 문구 · 특징 표시, 한글 검색
- 카드 효과 지원 상태 표시 (배지 · EFFECT 필터 · 덱 지원률)
- 수정: 카드 캐시가 120장으로 덮어써지던 문제, 점검 지적 4건

## v0.4.0 — 2026-10-20 (`f2c90dc`)
효과 엔진 1~4단계
- 카드별 효과 스크립트 → 키워드 사전 → 영어 효과 문장 파서 → 대상 · "~할 수 있다" 선택 창과 AI 평가 함수
- 옵션 카드 색 조건, 덱 이름 지정 · 여러 덱 저장

## v0.3.0 — 2026-10-20 (`46e7c92`)
실제 카드 · 공식 룰
- 내장 카드 120장을 실제 카드로 교정 (공식 한글판 이름 · 효과 문구, 실제 수치 · 등급), 전체 도감 API 동기화
- 공식 룰 대전 엔진: 실제 카드 효과 · 플레이어 블록 선택 · 게임 준비 순서(멀리건 후 시큐리티)

## v0.2.0 — 2026-10-19 (`7de0840`)
- 대련장(공식 플레이매트 배치) · 로비 · 바인더 덱 빌더 디자인 개편, 코인 토스, 상대 턴 리플레이, 승패 화면

## v0.1.0 — 2026-10-17 (`93ce6ea`)
- 첫 버전: 실전 AI 듀얼 아레나 · 1인 훈련소 · 카드 도감 & 덱 빌더
