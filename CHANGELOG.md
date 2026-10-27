# 변경 기록 — DIGIMON TCG : DIGITAL COLOSSEUM

버전 번호는 [`src/DigimonTcg.Version.props`](src/DigimonTcg.Version.props) 한 곳에서 관리합니다.
앱 로비 오른쪽 아래와 창 제목에 `v0.8.1 (커밋 해시)` 형식으로 표시됩니다.

**규칙** (SemVer 0.x — 정식 출시 전)
- 새 기능 · 규칙 변경을 묶어 배포하면 **MINOR**를 올린다 (0.8.x → 0.9.0)
- 버그 수정만 배포하면 **PATCH**를 올린다 (0.8.0 → 0.8.1)
- 올린 커밋에 git 태그 `vX.Y.Z`를 붙이고, 이 파일 맨 위에 기록한다

---

## v0.42.0 — 2026-10-27
미지원 효과 문장 16차 묶음 (조건이 붙은 진화 조건 무시 진화 · 효과로 진화 조건 무시 진화 · 셈 · 앞 문장의 코스트 더 줄이기 · 겹쳐진 카드 보호 · 시큐리티 · "대신" · 소멸시킨 디지몬의 코스트만큼 감소 · [육성] · 효과로 링크) — 도감 효과 문장 지원 9,451 → 9,555 / 10,366 (91.2% → 92.2%, +104)
- 모든 절이 동작하는 카드 3,535 → 3,620장 (수정 전 지원되던 문장 중 깨진 것 0, 한글이 바뀐 것 0). 새로 지원한 104문장 중 94문장에 생성 한글이 붙는다 (나머지 10문장은 영어 데이터의 깨진 중복 문장 — 아래, 등록하는 효과 없음)
- 읽는 방법: 15차와 같이 원래 규칙이 읽지 못한 절 · 문장 · 동작 · 셈만 16차 규칙으로 다시 읽는다 (SentenceParser.TryParse → 실패하면 Batch42). 한글 렌더러도 원래 규칙이 옮기지 못할 때만 16차 규칙을 쓴다
- 조건이 붙은 진화 조건 무시 진화 (지속 효과 — 조건은 진화할 때마다 지금 필드로 판단, 엔진: CardInstance.LiveContext42)
  - 진화할 카드 쪽: BT2-111 (공식 "자신의 트래시가 10장 이상일 동안, 자신의 「임프몬」은 패의 이 카드에 진화 조건을 무시하고 진화 코스트 4로 진화할 수 있다") · EX2-022 · BT14-101 · BT15-101 · RB1-009 ("진화원에 명칭에 「감마몬」을 포함하는 카드가 있는 자신의 「감마몬」")
  - 진화하는 디지몬 쪽: ST7-03 · ST8-04 (공식 "【자신의 턴】 Lv.6 이상인 상대의 디지몬이 있을 때, 이 디지몬은 ~ 패의 「듀크몬」으로 진화할 수 있다" — 명칭이 같은 카드만) · BT6-060 · BT23-026 · BT21-010 · BT21-040 ("A 또는 B" 조건) · ST20-10 · ST21-10 ("자신의 테이머의 색 합계가 3색 이상") · BT25-082
- 효과로 진화 조건 무시 진화의 다른 표기: "1 of your [Agumon] digivolves into this card" (BT22-013 · BT22-026) · "You can digivolve your [Agumon] into 1 [Agumon - Bond of Bravery] ~ ignoring its level" (BT6-087 · BT6-088 — 공식 "Lv.을 무시하고 진화 코스트를 지불하여") · "you may digivolve that Digimon into this card for a cost of 3" (BT13-028) · BT13-098 · P-092 · P-029 · P-030 ("If it does" = 이 효과로 진화했다면) · EX2-065 ("you may suspend this Tamer to ~" = 이 테이머를 레스트시키는 것으로) · "that Digimon digivolves into this card, ignoring level" (BT27-064 — 진화 코스트는 이 카드의 것, 색 조건은 본다) · BT24-084
- 셈: "N suspended Digimon and/or Tamers" (BT12-057 — 공식 "레스트 상태인 디지몬과 테이머 5마리(명)마다", 양쪽) · "2 of your opponent's suspended Digimon or Tamers" (BT13-060) · "2 of your Tamers' colors" (AD1-014 · BT21-061) · "color among your opponent's Digimon and Tamers" (BT18-079 · BT18-006) · "yellow and/or red Tamer you have in play" (BT12-104) · "Digimon your opponent has with no digivolution cards" (BT7-024) · "of this Digimon's level 6 digivolution cards" (BT15-102) · "of this Digimon's color" (BT8-084) · "A and each B" (BT7-108 · BT13-030 — 두 셈의 합) · "Option card in you and your opponent's trashes" (BT7-015) · "For each card this effect trashed" (BT26-083 — "Trash all of your security cards"가 파기한 매수)
- 앞 문장(진화 · 등장)의 코스트를 더 줄인다: "For every 10 total cards in both players' trashes, reduce this effect's digivolution cost by 1" (BT21-076 — 공식 "서로의 트래시 합계 10장마다 이 효과로 지불하는 코스트 -1") · BT21-082 · AD1-022 · AD1-019 · P-166 · BT27-049 · EX13-070 ("paid cost") · "further reduce it by 1" (EX5-062 · EX13-043) · "If ~, further reduce it by 3" (EX5-043 — 공식 "이 효과의 코스트 경감 수치")
- 겹쳐진 카드
  - 보호 (공식 BT21-060 "상대의 턴 종료까지 이 디지몬에 겹쳐져 있는 카드는 상대의 효과로 파기되지 않는다"): BT21-060 · BT26-085 · BT26-029 · EX12-059 — 상대 효과의 ≪퇴화≫ · 진화원 파기 · 맨 위 카드 파기를 막는다 (자신의 효과는 막지 않는다)
  - "Trash the top 10 stacked cards of 1 of your opponent's Digimon" (BT21-030 — 공식 "겹쳐져 있는 카드를 위에서부터 10장 파기한다"): 맨 위 카드부터 파기하고, 마지막 1장까지 파기하면 그 디지몬은 트래시로 (소멸이 아님 — "떠나려 할 때" 효과로는 막을 수 있다)
- 시큐리티: "trash the top 2 cards of your security stack" (BT6-087 · BT6-088) · "Look at the top card of your security stack, and you may add it to your hand" (BT9-034) · "Look at the top card ~. You may add that card to your hand to ＜Draw 1＞" (BT3-034 — 공식 "그 카드를 패에 추가하는 것으로, 《1 드로우》") · [시큐리티] "Return this Digimon to your hand at the end of the turn" (EX4-013 — 배틀하지 않고 등장한 이 디지몬을 이 턴 종료 시 패로)
- "대신": "A. By X, B instead." · "By X, instead B." (EX3-072 · BT19-092 · BT22-034 · BT12-031 — 공식 "자신의 디지몬 1마리를 소멸시키는 것으로, 대신 ~": 비용을 내면 A 대신 B, 내지 않으면 A) · "If C, B instead."의 B를 앞 문장에 맞춘다 ("Delete 2 such Digimon" P-088 · "1 opposing Digimon" BT14-089 · 빠진 "from your trash" BT7-109)
- 소멸시킨 디지몬의 등장 코스트만큼 감소 (공식 BT13-103 "소멸시킨 디지몬의 등장 코스트의 수치만큼, 지불하는 등장 코스트를 마이너스한다"): 이 카드 BT25-076 · 다른 카드 (테이머) BT13-103 — 비용을 낸 뒤 줄이는 양을 정한다 (FieldPlayCostModifier.PaidAmount42)
- [육성] 효과 (육성 에어리어에 있는 동안만 — EffectSource.Breeding42 · BreedingInherited42): BT13-007 (공식 "[육성]【자신의 턴】자신의 디지몬 전부는 진화할 수 없다") · BT23-072 ([자신의 메인 페이즈 개시 시] 진화원에서 등장) · BT20-083 (시큐리티가 줄었을 때) · EX10-013 (육성 에어리어에서 진화했을 때 — 공식 "{육성}【진화 시】이 디지몬을 이동시킬 수 있다"). 시점 효과는 [자신의 턴 개시 시] · [자신의 메인 페이즈 개시 시] · [턴 종료 시] · [진화 시] · 엔진의 사건(FireForSide), 지속 효과는 다른 디지몬에게 주는 것만
- 효과로 링크 (공식 ST22-12 "~ 디지몬 카드 1장을 이 디지몬에게 지불하는 코스트 -2 하여 링크할 수 있다"): BT21-097 · BT23-093 · EX10-070 ("1 of those Digimon") · BT25-075 ("up to 2 cards from your hand or trash") · BT25-089 · P-234 · ST22-12 · EX11-073 — 링크 조건 · 코스트는 카드의 것 (모르면 링크하지 않는다), 코스트를 낼 때는 메모리가 0 미만이 되지 않을 때만
- 원문 표기: "While this Digimon has [Shoutmon] in its name it gains ~" (BT11-015 · BT12-082 — 쉼표) · "has the [CS]," (BT23-002) · "Your other [Gaossmon] all get" (BT5-008) · "in battle. gain" (BT20-029) · "switch the attack target to 1 of your [Eosmon]" (BT17-074 · BT17-075) · "you can suspend this Tamer to place ~" (BT13-096 · BT8-092 · EX2-060)
- 영어 데이터의 깨진 중복 문장 (등록하는 효과 없음 — 진화원 효과는 메인 칸의 온전한 문장이 등록): 디지타마 EX13-001 · EX13-003 · EX13-004 · EX13-005 · BT25-005 · BT27-003 (진화원 칸에 이름이 빠진 같은 문장) · 「시스터몬」 BT6-084 · BT7-083 · BT23-077 · ST12-13 (진화원 칸이 "Card Effect(s) ~"로 시작하는 메인 칸의 복사본 — 공식 한글 데이터는 진화원 효과 없음)
- 동작이 바뀐 기존 카드: 「시스터몬」 4장(BT6-084 · BT7-083 · BT23-077 · ST12-13)의 진화원 칸 복사본 문장이 더는 진화원 효과로 동작하지 않는다 (예: ST12-13을 진화원으로 가진 디지몬이 "≪재기동≫ 부여"를 얻던 것 · BT23-077의 "레스트했을 때 ≪퇴화 1≫"). AI의 DNA 진화: 진화할 수 없는 디지몬(지속 효과 "can't digivolve")이 있으면 고르지 않고, DNA 진화가 처리되지 않으면 다시 고르지 않는다 (같은 DNA 진화를 끝없이 고르던 버그 — 시뮬레이션에서 발견). 한글 용어집에 특징 「히어로」 추가 (공식 BT21-010)
- 읽지 않고 남긴 것: BT11-034 (결정 대기) · 어플 합체 (BT21-084 · BT22-087 · BT23-079 · BT24-087 · P-241 — 엔진 처리 없음) · ≪블래스트 DNA 진화≫ · [육성] 11문장 (디지타마 덱 공개 · 떠나려 할 때 이동 등) · 상대 메모리 셈 (BT25-086 · BT26-090) · "If no Digimon was deleted by this effect, ~ instead" (BT13-111) · "trashing the top stacked card" 비용 · "top stacked card is trashed" 사건 (BT21-085 · BT21-094) · 영어 원문이 깨진 문장 (EX13-065 · EX13-066 · EX10-024 · BT23-052 · BT27-070 · BT27-084 · BT26-080 · BT27-085) · BT23-009 (영어 데이터에 【자신의 턴 종료 시】가 빠졌고, 링크 카드의 〈링크 중〉 시점 효과는 엔진 처리 없음)
- 테스트 37개 (EffectBatch42Tests)

## v0.41.0 — 2026-10-27
미지원 효과 문장 15차 묶음 (어플몬 【링크 시】 · 다른 표기 · "다음에 진화할 때" · 진화원 조작 · 조건 · 효과 발휘 · 링크 코스트 감소 · AI 링크 · ≪계승≫) — 도감 효과 문장 지원 9,337 → 9,451 / 10,366 (90.1% → 91.2%, +114)
- 모든 절이 동작하는 카드 3,444 → 3,535장 (수정 전 지원되던 문장 중 깨진 것 0). 한글은 테이머를 세는 말 "명" 외에 바뀐 것 0 (아래). 새로 지원한 114문장 모두 생성 한글이 붙는다
- 읽는 방법: 14차와 같이 원래 규칙이 읽지 못한 절 · 문장 · 동작 · 조건만 15차 규칙으로 다시 읽는다 (SentenceParser.TryParse → 실패하면 Batch41). 한글 렌더러도 원래 규칙이 옮기지 못할 때만 15차 규칙을 쓴다
- 어플몬의 【링크 시】 효과 — 영어 데이터에 〈링크〉 조건이 빠진 어플몬 카드(특징 "~ (App Name)", Lv.3 이상)의 진화원 칸 라벨 없는 문장 = 【링크 시】 효과 (공식 BT21-043 "【링크 시】상대의 턴 종료까지 ~"). BT21-018 · BT21-023 · BT21-043 · BT21-053 · BT21-054 · BT21-070 · BT21-071 · BT21-074 · BT22-009 · BT22-016 · BT22-035 · BT22-050 · BT23-016 · BT23-021 · BT23-028 · BT23-039 · BT24-032 · BT24-036 · BT24-038 · BT24-056 · BT24-071 · BT24-077 · BT25-007 · BT25-036 · BT25-045 · BT25-052 · BT25-056 · BT25-061 · BT25-070 · BT25-072 · BT26-019 · BT26-028 · BT26-037 · BT26-063 · BT26-084 · P-190 · ST22-12 (링크 조건 · 코스트는 그대로 공식 한글 데이터에서 읽는다)
- 다른 표기: "Draw 1" (괄호 없음) · "Until your opponent's turn ends, 1 of their Digimon ~" · "Until their turn ends" · "To 1 of your opponent's Digimon, give -4000 DP for the turn for each ~" · "To 1 of your opponent's Digimon, ＜De-Digivolve 1＞ for each ~" (BT24-041 · BT24-065 · EX9-043) · "From 1 of your opponent's Digimon, trash any 1 digivolution card for every ~" (EX9-018 · ST21-04) · "gets ＜Piercing＞ and ＜Blocker＞" (EX7-064 · BT8-043) · "-5000DP" (EX4-074) · "to their owner's hand(s)" (BT14-027 · P-073 · BT9-031) · "Digimon that has an [On Deletion] effect" (EX1-021) · "Gain 1 memory if you have ~" (EX9-066) · "For each card trashed (by this card's effect) / For each one, ~" (BT9-107 · EX6-060 · LM-067) · "give 3 of your opponent's Digimon ＜Security A. -1＞" · "At turn end, delete this Digimon" (BT23-025) · "add your top security card to the hand and place this card as the top security card" (BT27-099) · "[System|Seven Code]" 묶은 표기
- 보호 두 가지: "can't have its DP reduced by your opponent's effects, and isn't affected by ＜De-Digivolve＞ effects" (BT11-069 · BT16-055 — 공식 "《퇴화》 효과를 받지 않는다": 엔진은 상대의 ≪퇴화≫만 막는다) · "their ＜De-Digivolve＞ effects don't affect 1 of your Digimon" (BT24-055)
- "다음에 ~ 진화할 때" 진화 코스트 감소 (엔진에 이 턴 동안의 감소 — 다음 1번 진화에만): "The next time one of your Digimon would digivolve this turn, reduce the digivolution cost by 1" (ST12-15) · BT1-109 (공식 "이 턴 동안, 다음에 그린인 자신의 디지몬이 Lv.5에서 Lv.6으로 진화할 때") · EX1-033 · EX5-029 · BT3-103 (비용: 자신의 디지몬 1마리를 레스트)
- 자신의 어택 대상 변경: "if this Digimon is attacking, you may switch the target of the attack to 1 of your opponent's suspended Digimon" (BT9-052 · BT9-055)
- 진화원 조작
  - 상대의 디지몬을 다른 상대의 디지몬의 진화원 아래에 (BT11-088) · "또는 상대의 테이머 아래에" (EX10-056 — 놓일 다른 디지몬이 없으면 테이머 아래). 종합 룰 4-7-7: 맨 위 카드만 놓이고 진화원은 트래시
  - 맨 위 카드 파기: "Trash the top card of this Digimon" (BT9-083 · BT13-058 · BT13-091) · "trash this Digimon's top stacked card" (EX10-022) — 바로 아래 진화원이 맨 위 카드가 된다 (진화원이 없으면 하지 않는다)
  - 위치 표기: "at the bottom of one of your Digimon's digivolution cards" (BT7-027) · "at the bottom of this Digimon's digivolution cards" · "as digivolution cards of this Digimon" (BT12-111) · 시큐리티 "Place this Digimon at the bottom of your security stack face down" (EX4-060)
- 조건: "If digivolving from the trash" (BT10-080 · BT23-066 — 공식 "트래시에서 진화하고 있었다면") · "A or B" (BT9-075 · BT17-065 · BT17-067 · BT17-073 — 공식 "~ 있거나, ~") · "this Digimon has 4 or more [Vemmon] in its digivolution cards" (EX11-046) · "4 or more colors in its digivolution cards" (EX12-076) · "one of your [Mother D-Reaper]s has 5 or more digivolution cards" (EX2-053) · "a Digimon with ~ is in play" (BT6-111 — 서로의 디지몬) · "you don't have any Digimon other than Digimon with [Dark Masters] in their texts" (EX10-012 · EX10-020 · EX10-035 · EX10-057) · "When this Digimon had 2 or more colors" (BT9-074 · BT9-076 — 조건) · "your have" 원문 오타 (BT9-080 · BT9-081)
- 효과 발휘 (공식 BT18-066 "놓은 카드의 【등장 시】 효과 1개를 이 디지몬의 효과로서 발휘할 수 있다"): EX5-059 · EX5-061 ("activate this Digimon's [On Play] effects") · BT23-101 · BT11-016 · BT10-110 · EX3-065 · BT15-041 · BT11-029 · BT24-102 · BT18-066 · EX9-073 · P-147 · EX8-054 — 그 카드의 메인 효과를 발휘한다 (조건 · [턴에 1회]는 그대로, 효과 안에서 다시 발휘하는 것은 3단계까지)
- 링크
  - 링크 코스트 감소 (BT25-004 · BT25-045 "When a [Social], [Tool] or [Game] trait card would link to this Digimon, you may reduce the cost by 1"): 패에서 링크할 때 · 효과로 코스트를 내고 링크할 때. BT25-004는 메인 칸("Inherited Effect [Your Turn] ~")과 진화원 칸에 같은 문장이 있어 한 번만 등록한다
  - AI가 패의 카드를 링크한다 (메모리를 상대 쪽으로 넘기지 않는 것만, ≪분리≫의 지정 링크 카드 — [Seven Code] 등 — 를 그 디지몬에 먼저). 시뮬레이션 플레이어 쪽도 링크
  - 링크 조건 "[Appmon] trait" · 「어플몬」: 특징에 "(App Name)"이 있는 디지몬도 어플몬으로 본다 (영어 데이터는 어플몬의 특징 "Appmon"을 빠뜨린 카드가 많다 — 공식 한글 데이터가 있는 어플몬 디지몬은 모두 특징 「어플몬」). 예: BT26-051 「고미몬」을 「롤플레이몬」(BT26-010)에 링크할 수 있게 되어 ≪분리≫가 실제로 쓰인다
- ≪계승 (지정)≫ (BT26-032 "＜Succession ([Ceresmon])＞"): 가장 위의 지정된 진화원 카드 1장의 효과를 얻는다
- 한글: 테이머를 세는 말 "장" → "명" (공식 "자신의 테이머가 1명 이하라면" · "자신의 테이머 1명마다" · "상대의 테이머 1명을 소멸시킨다") — 수정 전 지원되던 문장 73개 (+ 미지원 문장 5개)의 한글이 바뀌었다. "테이머 카드 1장" · "테이머 아래의 카드 1장" · "디지몬 또는 테이머 1장" · "디지몬과 테이머 1장마다"는 공식도 "장"이라 그대로. 그 밖에 바뀐 한글 0
- 동작이 바뀐 기존 카드: 링크 조건이 "[Appmon] trait"인 링크 카드를 특징이 "~ (App Name)"뿐인 어플몬에도 링크할 수 있다 (위). AI의 링크는 새 동작 — 어플몬 덱의 AI가 패의 카드를 링크한다. "placing ~ from your hand or trash as this Digimon's bottom digivolution card" 비용이 놓은 카드를 기억한다 ("the placed card" — 이 카드를 쓰는 기존 문장은 없었다)
- 읽지 않고 남긴 것: BT11-034 (결정 대기) · 삭제한 디지몬의 코스트만큼 감소 (BT13-103 · BT25-076 — 비용으로 소멸시킨 디지몬의 코스트를 기억하는 처리 없음) · [육성] 시점 15문장 · 디지크로스 · 어플 합체 · DNA 진화 (90) · "Trash the top 10 stacked cards" (BT21-030 — 맨 위까지 파기) · 조건이 붙은 진화 조건 무시 진화 (BT2-111 · EX2-022 · ST7-03 · ST8-04 · BT6-060 · BT23-026) · 효과로 진화 조건 무시 진화 (BT22-013 · BT22-026 · BT6-087 · BT6-088) · 영어 원문이 깨진 문장 (EX13-001 · EX13-003 · EX13-004 · EX13-005 · EX13-065 · EX13-066 · EX10-024 · BT23-052 · BT27-070 · BT27-084) · BT23-009 (영어 데이터에 【자신의 턴 종료 시】가 빠졌다) · "can't trash its stacked cards" 보호 (BT21-060 · BT26-085 · EX12-059 — 엔진 처리 없음)
- 테스트 29개 (EffectBatch41Tests — 「라센몬」 ≪디지버스트≫ BT8-081 포함)

## v0.40.0 — 2026-10-27
미지원 효과 문장 14차 묶음 ("When ~" 사건 · 같은 명칭의 지속 효과 · 키워드 부여 · ≪분리≫ · ≪디코드≫ · 여러 Lv. 1마리씩 소멸 · 공개 · 시큐리티에서 진화 · 코스트) — 도감 효과 문장 지원 9,228 → 9,337 / 10,366 (89.0% → 90.1%, +109)
- 모든 절이 동작하는 카드 3,361 → 3,444장 (수정 전 지원되던 문장 중 깨진 것 0, 한글이 바뀐 것 1 — EX13-060, 아래). 새로 지원한 109문장 모두 생성 한글이 붙는다
- 읽는 방법: 13차와 같이 원래 규칙이 읽지 못한 절 · 사건 · 문장 · 비용 · 조건 · 대상 · 지정 카드만 14차 규칙으로 다시 읽는다 (SentenceParser.TryParse → 실패하면 Batch40의 같은 뜻 다른 표기 · 둘로 나눈 절). 한글 렌더러도 절 전체가 원래 규칙으로 옮겨지지 않을 때만 14차 규칙을 쓴다
- "When ~" 사건 (엔진에 새 시점)
  - ≪연계≫로 레스트했을 때 (EX4-032 · EX4-033 · EX4-034 — 공식 "자신의 디지몬이 《연계》의 효과로 레스트했을 때") · 상대의 디지몬이 메인 페이즈에서 액티브가 되었을 때 (BT9-089 — 액티브 페이즈는 아님) · 같은 Lv.의 디지몬으로 진화했을 때 (BT9-092)
  - 이 디지몬이 진화하려 할 때 (BT8-024 · BT14-018 "진화할 때 또는 배틀 에어리어를 벗어날 때" — 코스트를 내기 전) · 맨 위 카드가 효과로 진화원에 놓였을 때 (EX5-001 · BT22-006 "진화원 아래에" · EX5-065 — 공식 Q&A EX5-001 · EX5-065: 진화로 진화원이 된 것은 아님)
  - 진화원에서 「벰몬」이 덱 아래로 되돌아갔을 때 (BT11-065 · BT18-065 · BT21-058) · 이 테이머 아래에 퍼플인 카드가 놓였을 때 (BT10-093 — 엔진은 테이머 아래를 쪽마다 한 곳에 모은다) · 「라센몬」의 효과(≪디지버스트≫)로 진화원에서 파기되었을 때 (BT8-081)
  - "특징으로 「4대용」을 가진 디지몬이 등장했을 때 또는 「4대용의 시련」이 놓였을 때" (EX3-027 · EX3-034) · "효과로 상대의 패에 카드가 추가되었을 때 또는 테이머 아래의 카드가 파기되었을 때" (BT25-029) · "자신의 디지몬의 효과로 이 디지몬의 진화원이 늘어났을 때" (BT17-056)
  - 「보코몬」 이외의 「감마몬」이 기술되어 있는 디지몬이 벗어날 때 (LM-019) · 쉼표 목록 + "other than [Sea Animal]" 진화 코스트 감소 (BT25-010)
- 효과를 가진 디지몬을 보는 지속 효과 대상 — "All of your other Digimon with the same name as this Digimon ~" (P-045 · BT5-063 · EX1-051): 지속 효과의 받는 쪽 조건이 효과를 가진 디지몬(StaticEffect.SubjectOf)을 본다. 공식 Q&A BT5-063: 진화해 명칭이 바뀌면 효과를 잃는다 (계산할 때마다 지금 명칭과 비교)
- 키워드
  - ≪분리≫ (종합 룰 16-46 — BT26-010 · BT26-019 · BT26-028 · BT26-037 · BT26-051 · BT26-063 · BT26-084): 자신의 효과 이외로 벗어날 때 지정된 링크 카드 1장을 파기하는 것으로 벗어나지 않는다 (파기는 임의, "벗어나지 않는다"는 강제)
  - ≪디코드≫ · ≪디코이≫ 조건의 다른 표기: "[Junomon]/Lv.5 or lower w/[Iliad] trait" (BT26-083) · "[Lv.4 or lower w/[DS] trait]" (EX12-028 · EX12-044) · "[Deva] or [Four Sovereigns] trait" (EX5-050 — 공식 "《디코이 《특징「데바」/「4성수」》》")
  - 키워드 부여: "All Digimon played by this effect gain ~" (BT10-084) · 같은 조건의 두 지속 효과 (BT10-112) · "~ 가지거나 ~인 자신의 디지몬 전부" (BT13-056 · BT11-086) · "≪세이브≫가 기술되어 있는" (BT12-098) · "A and B, for the turn" (BT14-041 — 공식 "턴 종료까지 ~하고") · BT8-019 (소멸한 디지몬마다) · EX8-055 · EX11-063 ("~ and attacks" = 강제 어택) · EX13-064 · BT27-025 ("it also gains" = 앞 문장의 기간) · EX9-033 (토큰 또는 [Puppet]) · EX5-042 · [시큐리티] 지속 효과 (BT25-094 · BT25-095 · BT25-097 · BT25-099 · BT25-102 · EX8-071 — 공식 "[시큐리티]【서로의 턴】")
  - 두 동작 "A and B" (끝 · 앞의 기간은 양쪽에 — 공식 BT19-035 · EX8-073 "상대의 턴 종료까지 ~하고, ~"): BT19-035 · EX8-073 · ST12-14 · ST15-14 · EX6-030 · BT12-099 (앞 동작이 어택이면 나누지 않는다)
- 여러 Lv. 1마리씩 소멸 — "Delete 1 of your opponent's level 3, 1 of their level 4 and 1 of their level 5 Digimon" (EX7-071 — 공식 "Lv.3과 Lv.4와 Lv.5의 상대의 디지몬 1마리씩을 소멸시킨다") · BT15-081 · BT15-100 · BT19-070 · BT24-075 · EX7-056
- 공개 · 시큐리티
  - 등장 코스트 합계: "any number of ~ whose total play costs add up to 7 or less" · "up to 7 play cost's total worth of ~" (BT11-044 · BT11-056 · BT14-068 · EX7-047 — 공식 "등장 코스트 합계 7까지") — 합계 안에서 1장씩 고른다
  - "2 cards that are either black with ~ or with the [Twilight] trait" (BT10-058) · 덱 위 1장 공개 후 등장 · 나머지는 패에 (EX5-042) · 보기만 하는 공개 (ST13-16)
  - 상대의 시큐리티 맨 위 공개 (P-078 · RB1-027 — "디지몬 카드라면 / 이외라면", 위 또는 아래로)
  - 시큐리티에서 진화 (BT14-033 · BT14-093 · BT16-024): 플레이어는 시큐리티의 조건에 맞는 카드 목록에서 고른다 (카드 고르기 창). "If digivolved by this effect" · "If this effect digivolved" 조건 (BT17-011 · BT17-022 · BT17-085도)
- 코스트
  - 정해진 코스트: "its memory cost is equal to the number of cards in your security stack" (BT7-100 — 공식 Q&A: 0장이면 0 코스트) · 진화 코스트 (BT7-040 — 공식 Q&A: 시큐리티 매수에서 다른 감소를 뺀다, 0장이면 1)
  - 상대 효과로 진화 코스트 +1 (EX3-016 · EX3-019 — 진화원이 없는 디지몬, 코스트를 지불하지 않는 진화는 그대로) · "If you have no Digimon, instead reduce the costs by 2" (BT26-088) · "Play or digivolved into" (EX5-012 · EX5-020 · LM-009) · 두 문장의 등장 코스트 감소 (BT16-065 — 공식 Q&A: 상대의 「반쵸」 디지몬도) · 테이머 자신이 진화할 때 (BT7-089)
  - 비용: 시큐리티 위에서 N장까지 파기 (P-074 — 파기한 매수마다) · 진화하려는 디지몬이 아닌 자신의 디지몬 소멸 (EX2-064)
- 그 밖: 이름 안의 마침표 "[T.K. Takaishi]" · "[Thomas H. Norstein]" · "[J.P. Shibayama]"를 문장 끝으로 자르지 않는다 (BT13-025 · BT13-031 · BT25-021 · BT25-023 · BT25-096 · BT7-046 · BT7-047 · BT14-093) · "[Thomas H. Norstein] trait" 원문 오류 → 그 테이머 (BT25-021 · BT25-023) · 테이머만 진화 (BT21-091) · "Delete this Digimon at the end of the turn" (BT17-011 · BT17-022)
- 동작이 바뀐 기존 카드
  - "If you have a purple Digimon or Tamer" (EX6-016) — 전에는 아무 색의 테이머로도 조건을 채웠다 → 그 색의 디지몬/테이머 (공식 BT17-011 "블루/그린인 자신의 디지몬/테이머가 있다면")
  - 한글: EX13-060 "It gains ＜Rush＞" — "이 디지몬은" → "그 디지몬은" (이 효과로 등장한 디지몬 — 엔진은 v0.39.0부터 그 디지몬)
- 읽지 않고 남긴 것: BT11-034 (결정 대기) · 링크 코스트 감소 (BT25-004 · BT25-045 — 링크 코스트를 바꾸는 엔진 처리 없음) · 옵션 사용 코스트를 비용으로 줄이기 (BT25-049) · 삭제한 디지몬의 코스트만큼 감소 (BT13-103 · BT25-076) · 디지크로스 · 어플 합체 (BT12-112 · BT21-030 · BT19-087 · EX10-064 · EX4-062 등) · 대체 효과 (BT3-056 · BT10-084 첫 효과 · BT5-017) · ≪계승≫ (BT26-032 — "가장 위의" 지정 카드만 얻는 처리 없음) · "When your effects add to decks" (BT26-015) · [육성] 시점 15문장 · 턴 종료 조건 변경 (BT14-081 · BT17-069) · P-246 (원문 누락)
- 테스트 35개 (EffectBatch40Tests)

## v0.39.0 — 2026-10-27
미지원 효과 문장 13차 묶음 (강제 어택 규칙 · 여러 곳에서 등장 · 사용 · 이름 제외 · 상한 · 비용 · 시큐리티 · 테이머 아래에서 진화 · 정한 턴 종료 시) — 도감 효과 문장 지원 9,056 → 9,228 / 10,366 (87.4% → 89.0%, +172)
- 모든 절이 동작하는 카드 3,228 → 3,361장 (수정 전 지원되던 문장 중 깨진 것 0, 한글이 바뀐 것 0). 원래 지원하던 문장 중 한글이 없던 15문장(BT10-089 · BT10-090 "패 또는 테이머 아래에서" · BT21-060 · BT22-081 · BT22-082 "its digivolution cards" · BT16-014 · BT17-038 · BT23-085 옵션 사용 등)에 새로 붙었다. 새로 지원한 172문장 모두 생성 한글이 붙는다
- 읽는 방법: 12차와 같이 원래 규칙이 읽지 못한 문장 · 비용 · 조건 · 셈 · 대상만 13차 규칙으로 다시 읽는다. 한글 렌더러의 13차 동작 읽기는 문장 전체가 원래 규칙으로 읽히지 않을 때 다시 읽는 동안에만 켠다 (원래의 다른 읽기를 가로채지 않게)
- 룰에 맞춘 수정 — 강제 어택 (종합 룰 15-9-1 강제 처리 · 16-34-3 · 공식 Q&A BT21-081 · BT23-070 · BT20-102 · BT24-051 "가능한 만큼 어택합니다")
  - "that Digimon attacks" · "this Digimon attacks (a player / without suspending)" — 어택할 수 있으면 어택하지 않기를 고를 수 없다. 대상은 어택하는 쪽이 고른다 (시큐리티에 어택할 수 없으면 "고르지 않음"이 없다). AI 정책이 고르지 않아도 어택한다
  - 새로 지원: BT19-062 · BT20-090 · BT21-081 · BT23-070 · EX10-071 · BT19-080 · "1 of your Digimon may get +5000 DP for the turn and attack your opponent's Digimon" (BT24-051)
- 동작이 바뀐 기존 카드
  - EX7-013 "~ and that Digimon attacks" — 전에는 어택을 고를 수 있었다 → 강제 어택
  - "~ may gain ＜Rush＞ for the turn and attack ~" (BT16-077 · BT20-102 · EX12-029 · EX12-056 · EX6-027) — "may"는 효과 전체: 발휘할지 한 번 묻고, 발휘하면 어택은 강제 (전에는 얻는 것은 언제나 하고 어택만 고름)
  - 준 효과 "[Start of Your Main Phase] This Digimon attacks." (BT21-077 · BT23-032 · BT25-054 · BT27-064 · BT27-100 · EX10-008 · EX10-034 · EX12-016 · EX5-048 · P-183 · P-240 · ST15-16 등) — 플레이어가 어택 대상을 고른다 (전에는 이기는 대상이 있으면 묻지 않고 그 대상). 플레이어에게 어택할 수 없는 디지몬은 디지몬 중에서 고른다 (전에는 고르지 않으면 시큐리티)
  - "[On Play] effects on Digimon played by this effect don't activate" (BT2-081 · BT3-110 · BT5-078 · BT5-079 · BT5-081 · BT5-106 · BT5-107 · EX1-063 · P-020) — 그 디지몬의 【등장 시】만 막고, 다른 카드의 "디지몬이 등장했을 때" 효과는 유발한다 (전에는 함께 막혔다)
  - 앞에 고른 대상이 없을 때 "it" · "that Digimon" = 이 효과로 등장한 디지몬 (BT14-076 "that Digimon gains ＜Rush＞" — 전에는 아무것도 얻지 않았다)
- 여러 곳에서 등장 · 사용 (SentenceParser.Batch39Play)
  - "Play 1 blue level 3 Digimon card from 1 of your blue Digimon's digivolution cards ~" (EX3-022 — 공식 "블루인 자신의 디지몬 1마리의 진화원에서 ~") · BT11-085 · BT11-098 · BT12-025 · BT12-032 · BT13-096 · EX2-016 · EX3-023 · EX3-026 · EX6-014 · "any of your [Imperialdramon: Fighter Mode]'s digivolution cards" (BT20-094) · "that Digimon's digivolution cards" (BT17-084 · EX6-065)
  - "from your hand or this Digimon's digivolution cards" (BT24-065 · EX12-034 · BT13-040) · "from your trash or from one of your Digimon's digivolution cards" (BT17-025) · "from under any of your Tamers" (BT19-025 · BT19-102 · P-224)
  - "Return to the hand or play 1 ~ from this Digimon's digivolution cards" (BT18-017 · BT18-029 · BT18-055 · BT18-074 — 공식 "~ 패로 되돌리거나, 코스트를 지불하지 않고 등장시킬 수 있다")
  - 1장씩: "1 level 3 Digimon card and 1 level 4 Digimon card" (BT10-027) · ST9-06 · EX3-073 · EX4-021 · EX6-031 · BT7-063 (레스트 상태로) · "as another Digimon" (BT7-028 · BT7-096)
  - 옵션 사용: "Use 1 ~ Option card from your hand or this Digimon's digivolution cards" (BT25-085 · EX13-014 · EX13-061) · "Play or use 1 ~ card ~" (EX12-077 · EX13-045 · BT27-015) · 코스트를 내는 "Play or use 1 card with ~ from your hand with the cost reduced by 2" (EX12-013 · EX12-027 · EX12-041 · EX12-043 · EX12-050 · EX13-005 · EX13-012 · BT25-041 · BT26-006 · BT26-032 · BT26-033 · ST23-04 · ST23-08) · BT25-083 · BT26-026 · BT26-070 · BT26-074 · EX13-072
- 이름 제외 · 진화
  - "without [Justimon: Accel Arm] in its name" (BT11-073 · EX13-060 — 공식 "「저스티몬: 액셀 암」 이외의") · ", other than [Sea Animal]," (BT25-009 · BT25-012 · BT25-051 · EX13-038 · EX13-042) · "1 of your [Diaboromon] without [Doomsday Clock] in its digivolution cards" (BT17-100 · BT19-078 — 공식 "진화원에 「종말의 시계」가 없는")
  - 진화 다른 표기: "can digivolve" (BT11-073 · BT7-051 · EX1-035 · EX1-040) · "for 1 memory" (BT10-041) · "for the digivolution cost" (BT12-016 · EX3-063 · EX4-056) · "from your trash for the cost" (EX3-008 · EX3-058) · BT10-067
  - 테이머 아래에서 진화: "This Digimon may digivolve into [OmniShoutmon] under your Tamers without paying the cost" (BT19-008 · BT19-025 · BT19-033 · BT19-047 · BT19-057 — 공식 "자신의 테이머 아래의 「오메가샤우트몬」으로")
  - "1 of your Digimon or Tamers may digivolve into ~" (BT18-022 · BT18-048 · BT18-063 · BT18-076 · BT18-078 — 공식 "자신의 디지몬/테이머 1마리(명)를 ~ 진화시킬 수 있다")
- 등장 조건 · 상대가 등장
  - "Your opponent plays 1 level 4 or lower Digimon card from their trash suspended without paying the cost" + "[On Play] effects ~ don't activate" ("Any" 없는 표기) (EX5-060 — 상대가 고른다) · "with a level less than or equal to it" (EX5-060 — 공식 "등장한 디지몬의 Lv. 이하의")
  - 그 디지몬의 진화원 아래에: "~ from your hand as that Digimon's bottom digivolution card" (BT19-082) · EX8-067 · EX11-065
- 상한 · 셈 (원래 규칙이 읽지 못한 표기만)
  - "remove 2000 from this DP deletion effect's maximum" (BT15-089 — 공식 "상대의 시큐리티 1장마다 이 DP 소멸 효과의 상한 -2000") · "add 3000 to this DP-Based deletion effects maximum" (EX7-066 — "명칭이 서로 다른 ~ 1마리마다") · BT18-102 · LM-001 · BT23-014 ("상대의 디지몬과 테이머 1마리(명)마다") · EX8-074 ("레스트 상태인 다른 디지몬") · "reduce ~ by 1" · "reduce 1 from ~" (EX6-059 · EX7-062) · EX8-072 · P-114 ("자신의 「디아블로몬」 1마리마다")
- 비용
  - 상대 · 서로의 트래시: "returning 1 of each Digimon card with different levels from your opponent's trash to the top of the deck" (BT18-019 — 공식 "Lv.이 서로 다른 디지몬 카드 1장씩") · "returning 3 cards in trashes" (BT26-016 — 각자의 덱 아래로) · "1 [Kimeramon] and 1 [Machinedramon]" (BT18-019) · BT26-015 · P-243
  - "returning 1 [Dorumon] from those Digimon's digivolution cards to the hand" (BT20-097) · "~ from that Digimon's digivolution cards to the bottom of the deck" (BT18-092)
  - 진화 코스트 감소의 비용 "placing 1 card from under one of your Tamers as the bottom digivolution card of that Digimon" (BT12-087 · BT12-091 · BT12-093 · BT12-094 · BT12-096 · BT12-097 · BT21-088 — 진화하는 그 디지몬)
- 시큐리티
  - [시큐리티] "At the end of the battle, ~" (P-067 · P-068 · P-069 · P-071 · EX8-035 · EX1-027 · BT6-056 · EX1-065 — 공식 "배틀 종료 시, ~"): 시큐리티 디지몬의 배틀이 끝난 뒤 발휘. 뒤의 "Then, add this card to its owner's hand"도 배틀 뒤에 트래시에서 패로
  - "Trash the top card of both players' security stacks" (BT16-036) · "~ so that it has 1 card left" (BT21-098 — 공식 "상대의 시큐리티가 1장이 되도록 위에서부터 파기한다") · BT23-102 · "until there are 3 left; gain 1 memory for each ~" (BT6-033) · "They place 1 card from their hand as the bottom security card" (BT21-024 · BT24-016 — 상대가 고른다) · "Trash any 1 of your opponent's security cards" (BT24-018)
  - 다른 표기: "Trash 1 card from the top of your opponent's security stack" (BT13-020 · BT17-018) · "add the top card of your security stack to the hand" (BT14-032 · BT15-034 · BT18-038) · "add this card into your hand" (EX2-068) · "Place this Digimon on top of your security stack" (LM-007)
- 정한 턴 종료 시
  - "At the next end of your opponent's turn, return it to the hand" (BT16-084 · BT16-085 · BT16-088 · BT17-025 · BT17-069 · LM-013 — 공식 "다음 상대의 턴 종료 시, 그 디지몬을 패로 되돌린다") · "~ delete it" (BT16-074 · BT16-089) · "At the end of your opponent's turn, delete that token" (P-165)
  - "At the end of your opponent's (next) turn, play 1 [Ravemon] from your trash ~" (BT13-089 · EX4-058 · EX4-071)
- 한글: 사건 "any of your Digimon (with ~) attack" — 원래 규칙이 "자신의 ~"를 빠뜨린 경우만 고친다 (BT18-092 · BT19-082, 새로 지원한 문장) · "one of your [Galacticmon] attacks" (BT21-098)
- 읽지 않고 남긴 것: BT11-034 (결정 대기) · [육성] 효과 15문장 (육성 에어리어 시점 모델 없음) · 턴 종료 조건 변경 (BT14-081 · BT17-069) · "with the same name as this Digimon"의 지속 효과 (P-045 · BT5-063 · EX1-051 — 지속 효과의 대상 조건이 효과를 가진 디지몬을 볼 수 없어 읽지 않는다) · BT12-043 (디지몬 + 시큐리티 디지몬을 함께 고르는 대상) · BT15-078 (어택하는 쪽의 어택 대상 변경) · EX13-077 (색 2개마다 고르는 효과 · 진화원의 색 얻기) · 시큐리티 찾기 · 공개 (BT14-033 · BT16-024 · BT14-093 · P-078 · RB1-027) · EX4-013 (배틀 없이 등장한 시큐리티 디지몬을 턴 종료 시 패로) · P-066 · P-070 · BT6-111 (배틀 종료 시의 조건문) · BT18-096 · BT21-091 (테이머 진화의 다른 표기)
- 테스트 44개 (EffectBatch39Tests)

## v0.38.0 — 2026-10-27
미지원 효과 문장 12차 묶음 (다른 카드와 비교하는 대상 · 액티브 + 다른 동작 · 키워드 부여 · 비용 · 위 또는 아래 · [시큐리티]【상대의 턴 종료 시】) — 도감 효과 문장 지원 8,956 → 9,056 / 10,366 (86.4% → 87.4%, +100)
- 모든 절이 동작하는 카드 3,155 → 3,228장 (수정 전 지원되던 문장 중 깨진 것 0, 한글이 바뀐 것 0). 원래 지원하던 문장 중 한글이 없던 5문장(EX5-015 · EX5-018 · P-011 · P-047 "디지타마 카드 이외의 카드 N장을 덱 아래로 되돌리는 것으로" · BT18-018)에 새로 붙었다. 새로 지원한 100문장 모두 생성 한글이 붙는다
- 읽는 방법: 원래 규칙이 읽지 못한 문장 · 비용 · 조건 · 지속 효과만 12차 규칙으로 다시 읽는다 (ParseStep · ParseCost · ParseEventCondition · ParseStatic · 한글 렌더러 모두 "원래 규칙 → 실패하면 12차" 순서)
- 다른 카드와 비교하는 대상 (TargetSpec.Batch38 — 비용이 옮긴 카드를 비용 전후의 필드를 비교해 기억한다, RefCard38)
  - "with the same level as the placed card" (BT18-042 — 공식 "놓은 카드와 같은 Lv.의 상대의 디지몬 전부") · "~ as the returned card" (BT7-029) · "~ as that card" (EX5-026) · EX6-066 · EX9-055
  - "with a level less than or equal to the deleted Digimon's level" (BT11-076 · BT8-107 · BT7-078 — 공식 "소멸한 디지몬의 Lv. 이하의") · "whose level is less than or equal to the trashed card" (BT13-109 · BT15-099) · "the placed card's level" (BT15-029) · "as high or lower a level as the returned Digimon" (BT19-002) · "the deleted Digimon" 시점 (BT17-071 — 소멸한 그 디지몬) · "the played Digimon's level" (ST10-06)
  - "with a play cost less than or equal to the Digimon you played" (BT10-106 · EX3-054 — 공식 "등장한 디지몬의 등장 코스트 이하의") · "the trashed card's play cost" (BT9-102 · BT12-108 [시큐리티])
  - "with DP less than or equal to the Digimon played with this effect" (BT6-110 — 공식 "이 효과로 등장한 디지몬의 DP 이하의") · "the deleted Digimon's DP" (RB1-029 — 떠나기 전의 DP)
  - "with a level as high or lower than the number of cards in your security stack" (EX6-028 · BT8-042 — 공식 "자신의 시큐리티 매수 이하의 Lv.의") · "~ in your or their security stack" (BT16-063 — 큰 쪽) · "~ in this Digimon's digivolution cards" (RB1-031)
  - "Delete that Digimon and 1 of your opponent's Digimon with DP less than or equal to the chosen Digimon" (BT12-108 — 공식 "~ 상대의 디지몬 1마리와 선택한 자신의 디지몬을 소멸시킨다", 상대를 먼저 고른다) · BT17-067 · EX10-055 · "Choose 1 of your opponent's Digimon and return all ~ with the same level as it" (BT17-078)
  - 조건 "If your opponent has no Digimon with as many or more digivolution cards as that Digimon" (BT14-091 — 공식 "진화원 매수가 선택한 디지몬 이상의 상대의 디지몬이 없다면")
- 액티브 + 다른 동작 · 테이머 액티브
  - "Unsuspend this Digimon, and gain 1 memory for each red Tamer you have in play" (BT11-017 — 공식 "이 디지몬을 액티브로 하고, ~") · BT7-016 (셈 "digivolution card this Digimon has with the [Hybrid] trait") · BT8-038 · EX2-024 · "it gains ＜Security A. +1＞ for the turn and unsuspends" (BT18-018) · "1 of your Digimon unsuspends and gets +5000 DP" (BT27-067) · "[Security] Gain 1 memory, and add this card to its owner's hand" (BT9-109 · ST12-14 · BT9-098 · EX4-072)
  - 테이머를 액티브로 (새 동작): "Unsuspend this Digimon or 1 of your blue Tamers" (BT12-029) · "~ this Digimon or Tamer" (P-153) · "~ this Digimon and 1 of your Tamers" (P-048) · "unsuspend 1 of your Tamers" (BT10-009) · "1 of your Digimon and 1 of your [Kiyoshiro Higashimitarai]" (LM-004). "can't unsuspend" 상태의 테이머는 그대로
  - 조건이 붙은 이 디지몬: "you may unsuspend this Digimon with [Gallantmon] in its name" (BT17-013 진화원 — 공식 "명칭에 「듀크몬」을 포함하는 이 디지몬을 액티브로 할 수 있다") · EX13-002 · BT24-002 · BT27-015 · "this Digimon and 1 of your Digimon with [Greymon] in its name may unsuspend" (AD1-012)
  - "Until the end of your opponent's turn, 1 of your opponent's Digimon doesn't unsuspend" (BT13-053 · BT13-058 · BT13-060 · BT23-046 — 공식 "상대의 턴 종료까지 상대의 디지몬 1마리는 액티브가 되지 않는다") · "can't be unsuspended" (BT19-046) · "Delete 1 Tamer" (BT10-069 — 양쪽 테이머, 추천: 상대)
  - "When DNA digivolving, ~" = "If DNA digivolving, ~" (BT8-042 · BT8-015 · EX3-074 · ST9-11 — 공식 "조그레스 진화하고 있었을 때,")
- 키워드 부여 · 어택
  - 얻는 ≪가드≫ (EX12-072 · EX13-063) · ≪디코이 (블랙)≫ · ≪디코이 (레드/블랙)≫ (BT8-060 · ST12-12) — 효과로 얻은 키워드도 카드에 적힌 것과 같이 막는다 (다른 자신의 디지몬이 상대의 효과로 떠날 때 · 그 색의 디지몬이 소멸할 때, 이 디지몬을 소멸시키는 것으로)
  - 얻는 ≪천승≫ (BT26-030 "＜Execute＞ and ＜Ascension＞") — 소멸했을 때 맨 위 카드를 시큐리티 위에
  - "1 of your Digimon may gain ＜Rush＞ for the turn and attack a player" (BT16-077 · BT20-102 · EX12-029 · EX12-056 · EX6-027 — 공식 BT16-077 "턴 종료까지 자신의 디지몬 1마리는 《속공》을 얻고, 그 디지몬으로 플레이어에게 어택할 수 있다") · "~ gains ＜Security A. +1＞ for the turn and that Digimon attacks" (EX7-013 — 어택은 효과 어택과 같은 처리)
  - "gain ＜Piercing＞ and can also attack unsuspended Digimon" (BT20-019 지속 효과 두 문장) · "For the turn 1 of your Digimon gains ＜Raid＞ and gets +3000 DP" (BT20-071) · "1 of your [Rasenmon] gains ~" (BT7-100) · "To 1 of your opponent's Digimon give -2000 DP ~" (BT21-036) · "This Digimon with 5 or more digivolution cards may attack without suspending" (AD1-007)
- 비용
  - 여러 곳에서 고르기: "trashing 1 [Mineral] or [Rock] trait card from your hand or your Digimon's digivolution cards" (EX11-038 · EX11-065) · "~ in your hand or this Digimon's digivolution cards" (BT11-041 · BT15-065) · "trashing 1 Option card from any of your Digimon's digivolution cards or link cards" (BT25-085) · BT25-092 · "returning 3 cards with the [Three Musketeers] trait from your hand or trash to the top of the deck" (EX7-043 — 공식 "자신의 패/트래시에서 ~ 3장을 덱 위로 되돌리는 것으로"). 추천: 트래시 → 진화원 효과가 없는 진화원 → 패
  - "deleting 2 of your level 5 or higher Digimon with [Myotismon] in their texts" (EX10-011) · "deleting 1 Token" (EX11-012) · "deleting 1 of your Digimon with the [Composite] trait Digimon" (BT19-075) · "suspending 1 of your yellow or red Tamers" (BT21-045) · "returning the top card of one of your [Leopardmon: Leopard Mode] to hand" (BT13-107) · "trashing 1 card from your hand with the same level as the deleted Digimon" (EX4-052) · "trashing 1 of your Digimon with the [Machine] or [Cyborg] trait from your hand" (BT12-108)
- 위 또는 아래
  - 진화원 위 또는 아래에 놓기: "placing 3 Digimon cards with [Gammamon] in their texts from your hand or trash as this Digimon's top or bottom digivolution cards" (AD1-007 비용) · EX12-077 (자신의 디지몬 1마리) · EX12-018 (동작) · "under this Digimon as its top digivolution card" (BT11-071) · 트래시에서 "under this Digimon as its bottom digivolution cards" (BT11-065 · BT11-111). 위 · 아래는 고른다 (추천: 아래)
  - "trash the top or bottom card of their security stack" (BT16-056 — 공식 "상대의 시큐리티를 위 또는 아래에서부터 1장 파기한다", 추천: 상대 = 위 · 자신 = 아래) · "Place the top card of 1 of your opponent's [Vaccine] trait Digimon on top of their security stack" (BT16-056 — 진화원이 없으면 그 디지몬이 시큐리티로)
- [시큐리티]【상대의 턴 종료 시】 "Play this card without paying the cost" (BT20-052 · BT20-055 · BT26-082 · EX11-041 · EX11-043 — 종합 룰 15-14-5-1: 시큐리티에 앞면으로 있는 동안 유발. 상대의 턴 종료 시 자신의 시큐리티의 앞면인 그 카드를 코스트 없이 등장, 한글 표시 "[시큐리티]【상대의 턴 종료 시】")
- 룰에 맞춘 수정 (기존 카드 동작이 바뀜): 없음 (원래 지원하던 문장은 그대로 — 새 규칙은 읽지 못하던 문장에만)
- 읽지 않고 남긴 것: [육성] 효과 15문장 (육성 에어리어에서 유발하는 시점 모델이 없다) · 턴 종료 조건 변경 (BT14-081 · BT17-069 — 플레이어 · AI 턴 진행의 메모리 판정 여러 곳) · 상한 · DP의 "for each" (BT12-043 · EX7-066 · EX13-077) · BT11-073 ("without [X] in its name" 카드 고르기) · BT18-019 · BT20-097 · BT26-016 (상대 트래시 · "those Digimon" 비용) · EX5-060 (등장 조건 "level less than or equal to it") · EX7-023 · BT19-100 · BT11-107 · BT16-065 · BT9-105 · BT15-081 · 효과로 얻는 ≪디코이 (특징)≫ (P-045) · BT11-034 (결정 대기)
- 테스트 34개 (EffectBatch38Tests)

## v0.37.0 — 2026-10-27
미지원 효과 문장 11차 묶음 (메모리 조건 · DP 소멸 상한 · 등장 제한 · 색 · 시큐리티) — 도감 효과 문장 지원 8,872 → 8,956 / 10,366 (85.6% → 86.4%, +84)
- 모든 절이 동작하는 카드 3,092 → 3,155장 (수정 전 지원되던 문장 중 깨진 것 0, 한글이 바뀐 것 0). 원래 지원하던 문장 중 한글이 없던 3문장(BT8-001 · BT8-008 · BT10-011 "이 디지몬의 DP가 N 이상일 때")에 새로 붙었다. 새로 지원한 84문장 모두 생성 한글이 붙는다
- 메모리 조건 지속 효과 (StaticContext.Engine — 효과 주인 기준 메모리, 메모리가 바뀌면 지속 효과를 바로 다시 계산)
  - "[Your Turn] While you have 3 or more memory, this Digimon gains ＜Security A. +1＞" (BT1-018) · "While you have 0 or less memory, this Digimon isn't affected by ~" (BT17-016 · EX8-073)
  - "[All Turns] While you have 1 or more memory, none of your opponent's Digimon can suspend" (EX8-026 — 공식 Q&A: 자신 쪽의 1 이상, 레스트할 수 없으니 어택도 할 수 없다) · "~ can activate [When Digivolving] effects" (EX8-035) · "While ~. While you have 1 or less, none of your opponent's Digimon can activate [On Play] effects" (EX8-029 — 지속 효과 문장 여럿)
  - 줄인 메모리 조건 "If they have 4 or more, ~" · "Then, if you have 1 or less, ~" (BT15-016 · BT25-019 · LM-041)
- DP 소멸 효과의 상한 (공식 Q&A BT12-001: "DP 소멸 효과에 기술된 수치의 상한을 플러스") — 소멸 효과를 처리하는 동안만 "with N DP or less"의 N에 더한다 (되돌리기 · 레스트 등 다른 효과는 그대로)
  - "add 2000 to this Digimon's DP-based deletion effects' maximums" (이 디지몬의 효과만 — BT17-008/010 · BT19-007/009 "While you have 0 or less memory" · EX13-007/010)
  - "Add 1000 to the maximum DP you can choose with DP-based deletion effects" (자신이 발휘하는 효과 전부 — BT12-001 · BT9-011 · EX2-010 · EX2-011 "While you have a red Tamer")
- "can't play (or move) Digimon with N DP or less" — 보통 등장 · 효과로 등장 모두 (Q&A EX3-012), 이동은 육성 에어리어에서 배틀 에어리어로 (Q&A EX7-014). 화면 · AI도 막는다
  - "While your opponent has 1 or more memory, they can't play Digimon with 6000 DP or less, and this Digimon gets +4000 DP" (BT14-017 — 지속 효과) · "your opponent can't play Digimon with 5000 DP or less until the end of their turn" (EX3-012) · "~ can't play or move ~" (EX7-014)
- 색 · 이름 취급
  - "[Your Turn] This Digimon is also treated as yellow" (BT3-014 · BT3-040 · BT4-017 · BT6-013 · BT6-061 · BT6-077 · EX1-005 — Q&A BT3-014: 진화 조건 「옐로 Lv.5」로도 진화할 수 있다). 색 조건 대상 · 옵션 사용 색 조건에도 들어간다 (CardInstance.Colors)
  - "This Digimon is treated as also having the color of its digivolution cards. While this Digimon has 4 or more color, it gets +4000 DP" (BT8-084 — 앞면 진화원의 색)
  - "This Digimon is also treated as Lv.6 [Slayerdramon] for [Examon]'s DNA digivolution" (BT20-025 · BT20-042 · EX13-021 · EX13-041 — Q&A BT20-025: 배틀 에어리어에 있는 동안만)
  - "it doesn't activate [Security] effects on Option cards it checks" (BT7-014 · BT17-014 · BT20-015 · BT20-071 · BT20-074 · ST13-05 — 조건 · 특징이 붙은 표기)
  - "While this Digimon has 10000 DP or more, it gains ~" (BT17-079 · BT7-085 — 지속 효과 문장 여럿)
- 진화원 · 이 카드를 진화원에
  - "Trash the top or bottom digivolution card of 1 of your opponent's Digimon" (BT27-020 진화원 효과 — 디지몬마다 위 · 아래를 고른다, 추천: 진화원 효과 · DP가 큰 쪽)
  - 옵션 【메인】 "Then, place this card as the bottom digivolution card of 1 of your [Three Musketeers] trait Digimon" (EX7-070 · P-180 · BT9-109 · BT20-099) — 사용 뒤 트래시 대신 진화원
  - [패] 【메인】 "If you have a Digimon with [Justimon] or [Raidenmon] in its name in play, you may pay 1 memory to place this card under that Digimon as its bottom digivolution card" (BT9-029 · BT9-042 · BT9-054 · BT10-025 — 공식 "1코스트 지불하는 것으로, 이 카드를 그 디지몬 1마리의 진화원 아래에 놓는다". 놓을 디지몬이 없으면 메모리를 내지 않는다)
- 시큐리티
  - "Place 1 of your opponent's Digimon face down at the bottom of their security stack" · "~ as their top security card" · "~ at the top or bottom of ~" (RB1-019 · BT14-094 · BT14-102 · BT10-101 · BT27-099 · ST10-14 · EX11-016 — 위 또는 아래는 고른다, 추천: 아래)
  - "1 of your opponent's Digimon and all of your opponent's Security Digimon get -3000 DP for the turn" (BT7-098 · EX1-030 · EX4-009 · P-053 · ST17-06 · LM-040)
  - "[On Deletion] Place this card at the bottom of your security stack" (BT14-038 · BT14-102 · LM-042) · 옵션 "Then, ~ place this card at the bottom of your security stack" (BT16-100 · BT18-098 · BT15-092)
  - "[Security] Play this card without battling and without paying the cost" (BT5-112 · EX2-054 · "If your opponent has a level 6 or higher Digimon, ~" BT24-015 · BT24-039 — 공식 "배틀을 진행하지 않고 등장시킨다". 조건이 맞지 않거나 등장시킬 수 없으면 보통 배틀)
- 등장 제한: "This effect can't play [Plutomon]" (BT26-059) · "This effect can't play cards of the same level" (P-220 — 1장씩 고르며 같은 Lv.은 후보에서 뺀다)
- 원문 오타 "＜Security A. +1}＞" (BT24-072 · BT24-075)
- 룰에 맞춘 수정 (기존 카드 동작이 바뀜)
  - 메모리가 바뀔 때(효과로 얻기 · 잃기 · 진화 · 옵션 · 링크 코스트)마다 지속 효과를 다시 계산한다 (전에는 다음 행동 때)
  - 효과로 사용 중인 옵션이 자신을 다른 곳에 놓을 때 같은 카드가 트래시 · 패에 더 있어도 그 카드를 빼지 않는다 (시뮬레이션에서 찾음)
- 읽지 않고 남긴 것: 토큰 이름 제한 (BT19-091 · BT23-013 — 같은 문장의 "≪연계≫를 2번 얻고 어택" · "토큰 또는 카드 중 하나" 등장이 아직) · EX4-013 (턴 종료 시 패로 되돌리기) · "[Security] [End of Opponent's Turn] Play this card" (BT20-052/055 · BT26-082 · EX11-041/043) · BT12-043 · EX7-066 (상한 · DP의 "for each") · EX13-077 (색 수만큼 DP) · EX9-062 (어셈블) · BT15-060 · BT17-068 (공개 중의 이름 · Lv. 취급) · BT14-081 · BT17-069 (턴 종료 조건) · BT17-100 · 진화원 "top or bottom"에 놓기 (AD1-007 · EX12-018 · EX12-077) · BT11-034 (결정 대기)
- 테스트 29개 (EffectBatch37Tests) — KoreanEffectRendererTests의 "한글이 없는 문장" 예시를 아직 한글이 없는 문장으로 바꿨다

## v0.36.0 — 2026-10-27
미지원 효과 문장 10차 묶음 (효과로 등장 한 곳 · 플레이어 제한 · 테이머를 디지몬으로 취급) — 도감 효과 문장 지원 8,828 → 8,872 / 10,366 (85.2% → 85.6%, +44)
- 모든 절이 동작하는 카드 3,057 → 3,092장 (수정 전 지원되던 문장 중 깨진 것 0). 원래 지원하던 문장의 한글은 용어집 보강으로 바뀐 20문장과 BT14-090("이 디지몬을" → "그 디지몬을") 말고는 그대로. 새로 지원한 44문장 모두 생성 한글이 붙는다
- 효과로 등장 — 한 곳 (GoldfishEngine.EffectPlay.cs `TryPlayByEffect`)
  - 효과로 카드를 등장시키는 모든 길(패 · 트래시 · 진화원 · 테이머 아래 · 시큐리티 · 공개한 카드 · 토큰 · 육성 에어리어 · ≪불굴≫ · 시큐리티 디지몬/테이머 · 내장 스크립트)이 있던 곳을 알리고 한 곳에서 판단한다. 등장시킬 수 없으면 카드를 꺼내지 않는다 — 원래 있던 곳에 그대로, 공개한 카드는 남은 카드와 같이
  - 고를 때도 등장시킬 수 없는 카드는 후보에서 빠진다. 제한이 없으면 동작은 전과 같다 (전체 테스트 · 시뮬레이션으로 확인)
- 플레이어 제한 (공식 한글 · Q&A)
  - "Players can't play Digimon by effects" (BT14-009 · BT9-033 · BT9-047 — "서로는 효과로 디지몬을 등장시킬 수 없다", Q&A BT9-033: 옵션 카드의 효과도) — 토큰도 막고, 테이머는 막지 않는다
  - "Your opponent can't play Digimon by effects until the end of their turn" (BT8-097) · "~ Digimon or Tamers ~" (BT20-020) · "Until your opponent's turn ends, their effects can't play Digimon or Tamers from the trash" (BT23-014)
  - "Players can't reduce play costs" (BT8-071 · EX7-015 · ST12-03 · ST13-08 — Q&A: 등장 코스트를 지불할 때 마이너스할 수 없다, 양쪽 플레이어): 카드의 등장 코스트 감소 · 비용을 내는 감소 · 필드의 감소 · 디지크로스 감소 · 효과의 "with the play cost reduced by N" 모두 적용하지 않는다 (코스트 없이 등장 · 옵션 사용 코스트 · 진화 코스트는 그대로)
  - "Your opponent can't use Option cards until the end of their next turn" · "~ this turn" (EX1-072) · "[Opponent's Turn] While all of your Digimon are suspended, your opponent can't use Option cards" (BT8-057) — 패에서 사용 · 효과로 사용 모두 (Q&A EX1-072: 체크한 옵션의 【시큐리티】 효과는 사용이 아니라 그대로). 화면에서 사용하면 이유를 알려 주고, AI도 쓰지 않는다
  - "[Opponent's Turn] None of your opponent's Tamers can unsuspend" (BT17-048 · BT17-051)
- 테이머를 디지몬으로도 취급 — 「최건우」 (GoldfishEngine.TamerAsDigimon.cs) — 공식 Q&A: "DP X000인 디지몬"으로도 취급, 보통 디지몬과 같이 어택할 수 있다. 등장한 턴의 테이머는 그 턴에 어택할 수 없다
  - "For the turn, 1 of your [Marcus Damon]s is also treated as a 3000 DP Digimon and can't digivolve" (BT13-008) · "this Tamer ~" (BT12-092) · "~, can't digivolve, and gains ＜Blocker＞" (BT13-018 · BT17-087) · "~ ＜Rush＞ and ＜Alliance＞" (BT21-044) · "12000 DP ~. Then, that Digimon may attack your opponent's Digimon. Your opponent's unsuspended Digimon can also be attacked with this effect." (BT21-096) · AD1-021 · 지속 효과 "[Your Turn] All of your [Marcus Damon]s are also treated as 12000 DP Digimon and gain ＜Rush＞" (BT25-104)
  - 테이머 카드는 테이머 존에 그대로 있고 (테이머 효과 · "자신의 테이머" · 테이머 비용 그대로), 배틀 에어리어에 그 테이머를 나타내는 디지몬(DP · 키워드 · 진화 불가)이 하나 놓인다. 어택 · 블록 · 효과로 레스트하면 테이머도 레스트해 "자신의 테이머가 레스트했을 때"가 유발하고 (BT13-008 진화원 효과), 테이머가 레스트하면 그 디지몬도 레스트
  - 배틀 · 효과로 소멸하거나 패 · 덱 등으로 가면 테이머 카드가 그곳으로 간다 ("자신의 테이머가 소멸했을 때"도 — BT21-044 진화원 효과). 기간이 끝나면 디지몬 취급만 끝나고 테이머는 그대로
  - 화면: 배틀 에어리어에 그 디지몬이 DP와 함께 보이고, 테이머 칸 상태 줄에 "디지몬 DP 3000". AI도 어택 · 블록에 쓴다
  - 아직 하지 않은 것: 테이머 아래에 겹쳐진 카드의 진화원 효과 (테이머 아래 카드가 테이머마다 나뉘어 있지 않다) · Lv.이 없는 디지몬이라는 점 (Lv. 조건은 Lv.0으로 본다)
- 두 술어 보호 · 공개 + 상한 · 비용
  - "Until your opponent's turn ends, their ＜De-Digivolve＞ effects don't affect this Digimon, and it gets +1000 DP for each of its face-down digivolution cards" (BT22-060 — 공식 "상대의 《퇴화》의 효과를 받지 않고") · "their Digimon effects don't affect this Digimon, and it gets +3000 DP" (BT26-057 · EX10-010)
  - "you may delete 1 of your Digimon with the [Ver.3] trait and all of your opponent's Digimon with the lowest play cost" (BT26-055)
  - "For each of this Digimon's face-down digivolution cards, add 1 to the play cost maximum" (BT26-077 · EX10-074) · 공개 중간의 "~ add 1 to this effect's play cost maximum" (EX9-053)
  - 비용 "By trashing 1 card in your hand or placing it face down under any of your [Keenan Crier]s" (BT26-072) · "By placing 1 ~ from your hand as any of your Digimon's bottom digivolution card, or face down under any of your [DATA SQUAD] trait Tamers" (BT27-020) · "A and B" 둘 다 내는 비용 (EX13-071 · BT27-095 · BT23-084 · BT23-090) · "By suspending 1 of your [Rhythm]" (BT17-048 진화원)
- 진화할 때 비용을 내는 진화 코스트 감소: "When digivolving into this card, by suspending up to 5 Tamers, for each Tamer suspended by this effect, reduce the digivolution cost by 1" (BT17-048 — Q&A: 상대의 테이머도 고를 수 있다) · "When you would digivolve into this card, by returning up to 5 cards with the [D-Brigade] trait ~ to the top of your deck, reduce the digivolution cost by 1 for each returned card" (EX3-054)
- "1 each of level 3, level 4 and level 5" — "You may play 1 each of level 3, level 4 and level 5 Digimon cards with [Aqua] or [Sea Animal] in any of their traits from this Digimon's digivolution cards" (BT22-028) · 비용 "placing 1 each of level 4 and level 5 ~ from your trash as 1 of your [Kudamon]'s bottom digivolution cards" + "it may digivolve into [Kentaurosmon] in the hand or trash, ignoring level and with the cost reduced by 1" (EX13-071 — "it"은 진화원을 놓은 그 디지몬)
- "This effect can't play cards with the same name as any of your Tamers" (BT22-072 · BT24-034 · EX10-047 · EX10-051 — 공식 "이 효과로는, 자신의 테이머와 같은 명칭의 카드는 등장시킬 수 없다")
- 용어집 (공식 한글 효과문에서 확인, 공식 데이터가 있을 때만): 「데크스」([Dex] · [DeathX]) · 「윗체르니」 · 「데바」 · 「어둠의 4천왕」 · 「어플몬」 · 「반쵸」 · 「브이」 · 「무샤 유지」 · 「카자마 쇼토」. 같은 한글이 된 이름은 한 번만 (「데크스」/「데크스」 → 「데크스」)
- 룰에 맞춘 수정 (기존 카드 동작이 바뀜)
  - 효과로 등장할 때 있던 곳이 정확해졌다: "play this card (from your trash) with the play cost reduced by N" · 트래시의 이 카드 등장 · ≪불굴≫ · 코스트를 내는 등장도 "When you play a Digimon from your trash" 같은 "있던 곳" 조건에 맞는다 (전에는 알 수 없음)
  - 비용 "playing ~ to your empty breeding area"에서 카드를 고르지 않으면 비용을 내지 않은 것으로 본다
  - BT14-090 한글: "이 디지몬을" → "그 디지몬을" (진화원을 놓은 「아구몬」)
- 읽지 않고 남긴 것: "your opponent can't play Digimon with 6000 DP or less" (EX3-012 · EX7-014 · BT14-017 — 보통 등장까지 막는 제한, 다음 묶음) · "This effect can't play [Plutomon]" (BT26-059) · "~ can't play cards of the same level" (P-220) · 토큰 이름 제한 (BT19-091 · BT23-013) · "Trash the top or bottom digivolution card of 1 of your opponent's Digimon" (BT27-020 진화원) · 이름/특징 조각 「Glowing Dawn」 · 「Sovereign」 · 「Animal」 · 「Plant」 (공식 한글 데이터에 없거나 어느 것인지 가릴 수 없음) · BT11-034 (결정 대기)
- 테스트 35개 (EffectBatch36Tests)

## v0.35.0 — 2026-10-27
미지원 효과 문장 9차 묶음 (뒷면 진화원) — 도감 효과 문장 지원 8,733 → 8,828 / 10,366 (84.2% → 85.2%, +95)
- 모든 절이 동작하는 카드 2,991 → 3,057장 (수정 전 지원되던 문장 중 깨진 것 0). 원래 지원하던 문장의 한글은 그대로이고, 한글이 없던 4문장(ST23-13/14 · ST24-13/14 "테이머 아래에 뒷면으로")에 새로 붙었다. 새로 지원한 95문장 중 91문장에 생성 한글이 붙는다
- 뒷면 진화원 · 테이머 아래 뒷면 카드 모델 (종합 룰 Ver.4.3)
  - 4-7-4 아래에 겹쳐진 카드는 지정이 없으면 앞면 — "face down"이라고 적힌 것만 뒷면으로 놓는다 (전에는 모두 앞면으로 놓았다)
  - 4-7-9 뒷면 카드는 카드의 정보를 갖지 않는다 → 진화원 효과 · 진화원 DP · 키워드 · 이름 · 특징 · 색 · Lv 조건에서 빠지고, 정보를 보지 않는 조건("1 card" · "any 1 digivolution card")과 장 수("진화원 N장 이상" · "진화원이 없는")에는 들어간다
  - 4-7-10 주인은 자신의 뒷면 카드를 볼 수 있다 · 4-7-5 뒷면 카드는 겹쳐져 있음을 알 수 있도록 — 화면: 필드 진화원 띠가 실물 카드 뒷면(🂠 뒷면)으로 보이고, 툴팁 · 상세에는 자신의 것만 이름이 보인다. 효과로 카드를 고르는 창에서 상대의 뒷면 카드는 카드 뒷면으로 가린다. 테이머 아래 칩 · 상대 상태 줄에 뒷면 장 수
  - 16-41 ≪트레이닝≫은 덱 위 1장을 진화원 밑에 뒷면으로 · 17-1-3-2-4 에어리어의 뒷면 카드는 룰 처리로 파기 (≪퇴화≫ · ≪아머 퍼지≫ · 맨 위 카드 이동으로 뒷면 진화원이 맨 위가 되면 그 디지몬의 카드 전부를 파기 — 소멸이 아니다)
  - 엔진: CardInstance.FaceDownSources (뒷면으로 놓을 때 카드를 복사해 참조로 구분) · PlayerZones.FaceDownUnderTamers · GoldfishEngine.FaceDown.cs. DNA 진화 · 테이머에서 진화로 옮겨 가도 뒷면 그대로
- 새로 지원한 뒷면 문장 (EX9 Ver.N/DM · BT22 · BT26 · ST23/ST24 등)
  - 비용: "trashing this Digimon's bottom face-down digivolution card" (BT22-038/061/076 · EX9-031/041/063 · EX9-004/006) · "trashing any of your Digimon's bottom face-down digivolution card" (BT26-048) · "trashing 1 of your [DM] trait Digimon's bottom 2 face-down digivolution cards" (EX9-071) · "trashing its bottom 2 face-down or [Cyborg] trait digivolution cards" (EX9-073) · "trashing the bottom face-down card under any of your Tamers" (BT25-029/057) — 테이머 아래 비용은 뒷면 카드만
  - "A or B" 둘 중 하나를 내는 비용 (BT26-073 · BT26-082 · EX13-032): "by deleting this Digimon or trashing 2 bottom face-down cards from under any of your Tamers" — 둘 다 낼 수 있으면 고른다
  - 셈: "for each of its / this Digimon's face-down digivolution cards" (EX9-009/011/017/025/039/061/062/064 · BT26-040/043) · "From 1 of your opponent's Digimon, trash any 1 digivolution card for each ~" (EX9-018) · "To 1 of your opponent's Digimon, ＜De-Digivolve 1＞ for each ~" (EX9-043) · "It further gets -2000 DP for each ~" (EX9-030 — 앞 문장의 기간을 이어받는다)
  - 진화 코스트: "When any of your [Ver.3] trait Digimon would digivolve into this card, for each of their face-down digivolution cards, reduce the digivolution cost by 1" (BT22-038/061 · EX9-031/041/063) · "When this Digimon with face-down digivolution cards would digivolve into ~" (EX9-003) · "This Digimon with face-down digivolution cards may digivolve into ~" (EX9-001)
  - 시점: "When face-down cards are placed in this Digimon's / any of your Digimon's digivolution cards" (EX9-002 · EX9-069 · BT26-099) · "When effects trash face-down digivolution cards from your Digimon" (BT26-048)
  - 조건 · 대상: "if you have as many or fewer security cards as this Digimon has face-down digivolution cards" (EX9-029) · "All of your Digimon with face-down digivolution cards gain ＜Reboot＞" (EX9-069)
  - 놓기: "You may place 1 card from your hand face down as any of your [DM] trait Digimon's bottom digivolution card" (EX9-069) · "Place the top card of your deck face down under any of your [DATA SQUAD] trait Tamers" (ST24-03/09) · "Place your deck's top card face down under this Tamer" (EX13-071)
  - "[End of Your Turn] By placing 3 [Ver.2] trait Digimon cards from your trash face down as this Digimon's bottom digivolution cards, it may digivolve into ~ in the hand or trash" (BT22-049 · EX9-028/049/050/052) — "it" · "that Digimon" · "any of those Digimon"이 주어인 진화 (EX9-070 · BT26-099 · P-168 · EX11-069 · BT25-054 등)
  - "This [Glowing Dawn] trait Digimon unsuspends" · "This Digimon with the [Glowing Dawn] trait unsuspends" (ST23-04/08 · BT25-041 진화원 효과)
  - 카드 조건 "1 6000 DP or lower [Ver.4] trait Digimon card" · "1 play cost of 3 or lower [DM] trait card" (BT26-048 · BT22-052 · BT24-016/050 · BT25-055 · EX11-035 · P-243)
  - "After, ~" = "Then, ~" (그 후 — BT26-093/095 · EX12-070/071 · EX13-029/068/069 · BT25-080/084 · EX11-054)
- 그 밖 (3순위): "1 of their Digimon and 1 of their Tamers can't suspend" (BT23-092 · EX10-012 · EX5-067 — 공식 "상대의 디지몬과 테이머 1마리(명)씩은 레스트할 수 없다") · "Players can't gain memory other than by Tamer effects" (BT25-079 · EX13-050 — 양쪽 플레이어)
- 룰에 맞춘 수정 (기존 카드 동작이 바뀜)
  - ≪트레이닝≫ (EX9-008~EX9-061 · BT26-023/040 · P-202 등) · "placing ~ face down as this Digimon's bottom digivolution card" 비용 (EX9-010/026/037/038/051/059/060 · BT26-023) · 공개 후 "place 1 card face down as ~" (EX9-007/014/023/035/058) · 테이머 아래 "face down" (ST23-06/10/13/14/15 · ST24-02/04/13/14/15 · BT26-004/025/091/093/094/095 · BT27-095/096/098 · BT25-087 · EX13-026): 이제 실제로 뒷면 — 뒷면 진화원의 진화원 효과 · DP는 들어가지 않고, "the bottom face-down card from under any of your Tamers" 비용에는 앞면(≪세이브≫ · 크로스 하트) 카드를 쓸 수 없다. 9차 이전에 앞면으로 근사하던 ST23-10 · ST24-02 · BT26-004도 뒷면으로
  - BT19-051: 공식 한글 "자신의 디지몬 1마리는 상대의 효과로 패/덱으로 되돌아가지 않고" — 영어 원문에 빠진 "상대의 효과로"를 따른다 (자신의 효과로는 되돌릴 수 있다)
  - "Digimon or Tamers"인데 디지몬만 고르던 레스트 문장: "Suspend N of your opponent's Digimon or Tamers" · "N of your opponent's Digimon or Tamers can't suspend / unsuspend until ~" · 그 뒤 "It can't unsuspend ~"가 테이머도 고른다 (BT8-102 · BT9-100 · BT14-053 · BT18-053 · BT19-050/095 · BT20-035/042/044/084 · BT24-095 · BT25-028/053/058 · BT26-032/042/044/046/049 · EX7-022 · EX10-054 · EX11-031/036 · EX12-032/051/063 · EX13-016/040/041 등 약 60장). 테이머의 레스트 · 액티브 불가는 PlayerZones.TamerCannotSuspendUntil / TamerCannotUnsuspendUntil — 액티브 페이즈에도 레스트 그대로, "by suspending this Tamer" 비용도 낼 수 없다 (같은 카드 객체 단위라 같은 테이머 2장은 함께 묶인다)
  - "When ~ are placed in any of your Digimon's digivolution cards"의 "that Digimon" · "it"은 그 디지몬 (전에는 이 디지몬)
- 읽지 않고 남긴 것: "Players can't reduce play costs" · "Players can't play Digimon by effects" (효과 등장 · 코스트 감소 경로가 여러 곳이라 등장 출처를 한곳에 모은 뒤) · "1 each of level 3, 4 and 5" 여러 장 등장 (BT22-028 · EX13-071) · 진화할 때 비용으로 코스트 감소 "When digivolving into this card, by suspending up to 5 Tamers ~" (BT17-048 · EX3-054) · BT11-034 (결정 대기) · 테이머를 디지몬으로 취급 (별도 묶음) · BT22-060 · BT26-057 ("their ＜De-Digivolve＞ effects don't affect this Digimon, and it gets ~" 두 술어) · BT26-055 · BT26-077 · EX9-053 (공개 + 상한 늘리기) · BT26-072 · BT27-020 ("or placing it face down ~")
- 한글이 없는 새 지원 문장 4개: BT26-095 ("non-Digi-Egg card ~ face down under this Tamer") · EX10-009 (육성 에어리어 등장) · EX11-056 (부화) · EX11-068 ("If attacking by ＜Execute＞")
- 테스트 31개 (EffectBatch35Tests) — 전에 앞면으로 놓는다고 본 테스트 3개를 뒷면 모델로 고쳤다 (CostVariantTests 2 · EffectBatch33Tests 1)

## v0.34.0 — 2026-10-27
미지원 효과 문장 8차 묶음 — 도감 효과 문장 지원 8,318 → 8,733 / 10,366 (80.2% → 84.2%, +415)
- 모든 절이 동작하는 카드 2,696 → 2,991장 (수정 전 지원되던 문장 중 깨진 것 0). 원래 지원하던 문장의 한글은 용어집 보강(이름 조각 「시스터몬」 · 「체스몬」 · 「황제드라몬」 · 「드라몬」 · 「저스티몬」 · 「벨페몬」 · 「기즈몬」 · 「플러그인」 · 「3총사」, 특징 조각 「수생」 — 공식 한글 데이터가 있을 때만)으로 바뀐 113문장 말고는 그대로, 한글이 새로 붙은 것 46문장. 새로 지원한 415문장 중 412문장에 생성 한글이 붙는다 (아래 3문장 제외)
- 제한 · 보호 (약 135문장) — 기간 효과("until ~")와 지속 효과 모두 키워드처럼 얻는다
  - "can't attack players" (ST7-04 · BT11-037 · BT16-053 · BT21-053 · EX13-054 · BT3-105 · BT5-103 등) · "can't attack your opponent's Digimon" (EX2-036 · BT23-051 · EX1-056) · "can't attack or block" 지속 (BT19-077 · EX3-060 — 블록 불가 키워드 추가)
  - "can (also) attack your opponent's unsuspended Digimon" (BT8-018 · BT2-051 · BT17-060 · BT6-093 · ST12-08 · RB1-006 · P-058) · "~ with no digivolution cards" (EX1-016/018/020 · BT7-095) — 화면의 어택 대상 강조 · AI 어택 고르기도 같은 판정
  - "can't be deleted in battle" (BT14-028 · BT16-018 · BT19-023 · BT20-022 · P-098 · LM-003 · EX8-068) · "can't be deleted by your opponent's effects" (BT14-062 · ST13-13 · BT15-061 · BT18-063 · BT3-075 · RB1-027) · "Your opponent's effects can't delete this Digimon (or return it ~)" (EX7-041 · ST13-14)
  - "isn't affected by the effects of your opponent's Digimon" (EX5-074 · BT13-077 · BT15-047/049/053 · BT16-048 · P-140 · LM-024 · ST15-15) · "~ your opponent's effects" (EX13-062 · BT13-088 · BT20-019 · P-221 · EX9-021) · "Your opponent's Digimon effects don't affect ~" (AD1-018 · EX12-052 · ST23-09) — 효과 대상으로 고를 수 없다 (옵션 · 테이머 효과는 받는다)
  - "can't be returned to the hand or deck (by your opponent's effects)" (BT11-060 · BT17-038 · BT18-064 · BT13-076 · BT23-054 · BT22-058) · "can't have its DP reduced" (BT13-072 · BT6-053 · BT9-098 · BT16-093) · "can't activate [When Digivolving] effects" (BT20-033/034 · BT23-029/034 · ST22-04 · BT19-038 · EX12-036) · "None of their Digimon can unsuspend / suspend / digivolve ~" (BT16-025 · BT18-054 · BT25-028 · EX7-049 · LM-006)
  - "can't A or B" 두 제한 ("can't activate [When Digivolving] effects or suspend" · "can't digivolve or attack players") · "give 1 of their Digimon ＜Security A. -1＞ and it can't ~" · "This Digimon gets +2000 DP and can't be deleted ~" 같은 묶음
  - 조건부 주어 "This Digimon with the [Xros Heart] trait gains ＜Rush＞" (BT19-008/012/033/038/047/057/061 · BT21-011/021 · BT20-014 · BT22-011 · BT24-086 · BT25-074 · EX12-004 · ST20-01) · "This Digimon with 4 or more [Vemmon] digivolution cards gets +3000 DP" (BT21-006) · "This Digimon with no digivolution cards can't attack" (BT16-057)
  - 지속: "All of your Digimon with the [Angel], [Archangel] or [Three Great Angels] trait gain ＜Blocker＞" (쉼표 목록 — BT16-024 · EX6-021 · BT11-042/083) · "All of your other ~ get +1000 DP" (BT16-050/054 — 효과를 가진 디지몬 자신은 받지 않는다) · "All of your [Diaboromon] gain ＜Blocker＞" (EX1-065)
  - "Your opponent can't gain memory other than by Tamer effects" (BT3-046/061/077 · BT6-021 · BT18-009/059 · EX8-030 · ST21-02) — 상대가 효과로 메모리를 얻을 때 테이머 효과만
- "End the attack" (14문장 — EX6-045/048 · EX7-052/054 · EX9-024/027 · EX11-020/021 · BT5-111 · BT16-032 · BT21-060 · EX10-003 · P-089 · BT13-088): 배틀 · 시큐리티 체크 없이 어택을 끝낸다 (어택 종료 시 효과는 그대로). 플레이어 · AI 어택 모두
- 상대가 고르는 동작 (25문장): "Your opponent may trash 1 (Digimon) card in their hand. If they don't, ~" (BT15-074 · BT16-066 · BT13-102 · EX6-050/051 · EX8-063 · BT24-069) · "Your opponent may trash their top security card" (BT3-102 · BT19-043) · "Your opponent may delete 1 of their Digimon or Tamers. If this effect didn't delete, ~" (BT18-082 · EX6-054 · EX10-052/060) · "They trash 1 card in their hand" (EX4-058 · EX6-055 · LM-044 · BT26-076) · "Trash 1 card in your opponent's hand without looking" (BT14-075 · EX6-059) · "Search your opponent's hand, choose 1 card, and trash it" (BT13-092) · "Your opponent adds the top card of their security stack to the hand" (BT11-033 · BT17-028 · P-137) — 상대 AI는 패가 5장 이상 · 시큐리티 4장 이상 · Lv.3 이하 디지몬/테이머가 있을 때 응한다
- 배틀 (어택이 아닌, 9문장 — BT25-057 · BT26-047 · BT27-051 · BT25-020 · EX11-074 · EX13-044 · LM-066): DP를 비교해 진 쪽이 배틀로 소멸 (≪길동무≫ · "배틀에서 소멸시켰을 때" 포함)
- 대상 조건 (약 40문장): "with as many or fewer digivolution cards as this Digimon" (BT15-032 · BT16-027 · BT14-023 · AD1-025 · LM-022 · EX13-016 · BT25-103) · "with DP less than or equal to this Digimon" · "with as much or less DP as that Digimon" (BT17-076 · EX6-010) · "with as high or lower a play cost as this Digimon" (BT15-056/058 · BT24-054/055) · "with play costs less than or equal to this Digimon's digivolution cards" (BT7-065) · "with 2 or more colors" (BT8-095 · BT8-103) · "without [X Antibody] trait" (BT7-106 · BT9-110) · "Choose 1 of your Digimon. ~ with DP less than or equal to the chosen Digimon" (BT10-096 · BT7-093 · BT11-102 · BT14-092) · "with as high or lower a level as it" (BT19-027)
- 테이머도 고르는 대상 (18문장): "Delete 1 of your opponent's play cost 3 or lower Digimon or Tamers" (BT15-064 · BT20-078 · EX10-010 · EX5-054) · "Return 1 of your opponent's Tamers to its owner's hand / the bottom of the deck" (BT13-031 · BT9-096 · EX7-023 · EX8-045)
- 비용 (35문장): "trashing any 2 of this Digimon's digivolution cards" (EX10-034/055/058) · "trashing (any / up to) N [Mineral] or [Rock] trait cards from (any of) your Digimon's digivolution cards" (EX10-028/036 · EX11-044 · BT21-054/074) · "trashing any 1 digivolution card of your [Bagra Army] trait Digimon" (EX10-045) · "returning N cards from your opponent's trash to the bottom / top of the deck" (BT17-070 · BT14-061 · BT19-101 · RB1-028) · "returning 1 card with the [Hybrid] trait from this Digimon's digivolution cards to the hand" (BT7-025 · BT10-066/067 · BT5-087 · EX6-005) · "suspending 1 of your opponent's Digimon or Tamers" (BT13-057 · BT16-028) · "returning 1 of your yellow Tamers to the hand" · "~ [TS] trait Tamers to the bottom of the deck" (BT17-039 · BT26-092 · BT13-010) · "returning 1 other suspended Digimon to the bottom of the deck" (BT26-050) · "returning this Digimon to the hand" · "trashing the top 3 cards of your deck" (P-011) · "trashing the top or bottom card of your security stack" · "suspending up to 2 of your yellow Tamers"
  - "trashing up to 3 cards in your hand" · "returning up to 5 ~ from your trash" (원래 규칙): 정확히 N장이어야 했던 것을 1~N장으로 고치고, 낸 매수를 뒤의 "~ for each card trashed / you return this way"가 센다 (ST16-12 · BT25-008 · BT4-074)
- 등장 · 사용 코스트 (6문장): "When this card would be played, by placing up to 3 [Dark Masters] trait cards with different names from your battle area or trash under it, reduce the play cost by 4 for each one" (BT15-102 — 배틀 에어리어의 디지몬은 맨 위 카드만, 진화원은 파기) · "~ 1 of each face-up [Dark Masters] trait card with different names from your security stack under this card ~" (EX10-061) · "~ placing up to 13 ~ from your trash at the bottom of your deck, reduce the cost by 1 for each card" (BT17-060) · "When this card would be used, by trashing cards from the top of your security stack, up to having 3 left, reduce the cost by 2 for each card trashed" (BT16-100) · "When you would use this card, by placing ~, reduce the cost by 3" (BT12-102) · "~ returning 3 cards ~ to the top or bottom of the deck, reduce the play cost by 5" (BT23-057)
- 셈 (25문장): "for each color in its digivolution cards" (EX9-074 · P-185 · EX1-022 · EX12-076 · BT19-014 · BT18-018) · "for each of its colors" (ST9-11 · P-076) · "for each of its digivolution cards" (BT21-072) · "for each card with the [X Antibody] trait in this Digimon's digivolution cards" · "for every 2 [Argomon] in its digivolution cards" (BT17-051) · "for each card in your trash with the [Cyborg] or [Machine] trait" (EX2-036) · "for each [Eyesmon: Scatter Mode] in your trash" (BT7-072) · "for each other Digimon you have in play with the same name as this Digimon" (EX1-044) · "for each of your [Hudie] trait Digimon" · "for each suspended Digimon" (BT25-059 · LM-066)
- 그 밖: "This Digimon can only digivolve into white Digimon / [Apocalymon] / Digimon cards with [Lucemon] in their name" (BT15-031/052/066/079 · EX10-012/020/035/057 · BT4-115 — 진화 · 효과 진화 모두 막는다) · "Black also meets this card's color requirements" (LM-033~038 · LM-045~053 옵션) · "Place 1 [Raijinmon], [Fujinmon], and [Suijinmon] from your trash ~" (BT9-067 · BT19-031 · BT7-063 — 이름마다 1장씩) · "Trash up to 3 cards in your hand" · "Trash up to 3 cards from the top of your deck" · "Return 1 red Digimon card from your trash to the top of the deck" (LM-027~032 ≪딜레이≫ 선택지) · "Play 1 [ShootingStarmon] from under your Tamers" 한글 · 원문 오타 ("until the end your opponent's next turn" · "Diigmon" · "-5000DP" · "Unsuspended this Digimon")
- 생성 한글 (공식 표기): "이 디지몬은 플레이어에게 어택할 수 없다" · "이 디지몬은 상대의 효과로 패/덱으로 되돌아가지 않는다" · "상대의 턴 종료 시까지, 이 디지몬은 배틀에서 소멸하지 않는다" · "레스트 상태인 이 디지몬은 상대의 디지몬의 효과를 받지 않는다" · "이 디지몬은 액티브 상태인 상대의 디지몬에게도 어택할 수 있다" · "특징으로 「크로스 하트」를 가진 이 디지몬은 《속공》을 얻는다" · "진화원이 4장 이상 있는 이 디지몬을 DP +2000" · "상대의 디지몬 1마리의 【진화 시】 효과는 발휘하지 않고," · "상대는 본인의 패에서 디지몬 카드 1장을 파기할 수 있다. 이 효과로 파기하지 않았다면," · "상대는 본인의 디지몬/테이머 1마리(명)를 소멸시킬 수 있다" · "상대의 패 1장을 보지 않고 파기한다" · "그 어택을 종료한다" · "진화원 매수가 이 디지몬 이하인" · "자신의 디지몬 1마리를 선택한다. 선택한 디지몬의 DP 이하인 ~" · "이 디지몬의 진화원을 선택하여 2장 파기하는 것으로," · "이 카드가 등장할 때, ~ 이 카드의 아래에 놓는 것으로, 놓은 1장마다 지불하는 등장 코스트 -4." · "이 디지몬은 화이트인 디지몬으로만 진화할 수 있다" · "상대는 테이머의 효과 이외로 메모리를 플러스할 수 없다" · "「뇌신몬」과 「풍신몬」과 「수신몬」 1장씩을 ~ 놓는다"
- 읽지 않고 남긴 것: 뒷면 진화원 · 테이머를 디지몬으로 취급 (별도 묶음 예정 — 「마사루」 "All of your [Marcus Damon] ~"도) · "This Digimon is also treated as red" (디지몬마다 색을 더하는 모델이 없음) · 비용 뒤 "If this effect didn't ~" (비용을 내지 않았을 때 Q&A 없음) · 진화할 때 비용을 내는 진화 코스트 감소 (EX3-054 · BT17-048) · 플레이어 규칙 제한 ("Players can't reduce play costs" · "can't play Digimon by effects" · "can't use Option cards") · "with the same level as the placed / returned card" · 여러 대상 한 문장 ("1 of their Digimon and 1 of their Tamers" · "level 3, level 4 and level 5") · 메모리 조건의 지속 효과 ("While you have 0 or less memory") · 덱 위 공개 · 어플 합체 · DNA의 남은 표기
- 한글이 없는 새 지원 문장 3개: BT9-110 ("delete all Digimon without ~ instead") · P-167 (공개 후 "or place 1 such card") · BT18-018 (셈 + "trash any 1 digivolution card ~ and suspend ~")
- 테스트 59개 (EffectBatch34Tests)

## v0.33.0 — 2026-10-27
미지원 효과 문장 7차 묶음 — 도감 효과 문장 지원 7,916 → 8,318 / 10,366 (76.4% → 80.2%, +402)
- 모든 절이 동작하는 카드 2,424 → 2,696장 (수정 전 지원되던 문장 중 깨진 것 0 · 한글이 바뀌거나 사라진 것 0, 원래 지원하던 문장 중 한글이 새로 붙은 것 41문장). 새로 지원한 402문장 모두 생성 한글이 붙는다
- "~를 놓는 것으로" 비용 (약 110문장 — 비용 85 · 같은 규칙을 쓰는 "Place ~" 동작 약 30)
  - 다른 자신의 디지몬 · 테이머 → 이 디지몬의 진화원 아래 (BT15-029 · BT22-028 · BT24-059 · BT24-074 · EX8-024 · EX6-014 · BT12-027 · BT16-057 · BT19-028 · BT27-031 · BT23-040 · BT7-058) · 이 디지몬 → 다른 자신의 디지몬 아래 (ST13-02/03/09/11 · BT10-059 · BT22-018 · P-142 · P-112 · P-214 · P-239 · BT17-050 · EX2-028) · 이 테이머 → 디지몬 아래 (BT13-103 · EX10-066 · BT22-095)
  - 종합 룰 4-7-7 · 4-7-8: 진화원으로 놓이는 디지몬은 배틀 에어리어를 떠나고 그 진화원은 동시에 파기된다 — 맨 위 카드만 놓인다 (공식 한글 BT7-058 "진화원을 모두 파기하고, 이 디지몬의 진화원 아래에 놓는 것으로"와 같음). "떠나려 할 때" 효과가 발휘하고, 비용으로 놓는 것은 막을 수 없다 (동작 "Place ~"는 막을 수 있다). 링크 카드는 트래시, 토큰은 사라지고, 아래에 놓이는 카드는 ≪오버플로우≫를 처리하지 않는다 (4-19-4)
  - 이 테이머와 트래시의 [X] · [Y] 1장씩 (ST17-10 · BT12-089 · BT17-080 · BT17-085) · 트래시의 [X] · [Y] 1장씩을 자신의 [Z] 아래 / 테이머 아래 / 덱 아래 (BT15-091 · BT14-090 · BT25-096 · BT26-098 · BT27-101 · BT12-015 · BT17-014 · BT17-026 · BT18-026/053/070/081 · P-022)
  - 테이머 아래: 패 · 트래시의 카드 (BT19-001 · BT19-016 · BT19-081 · EX10-044 · EX10-064 · P-224 · ST23-10 · ST24-02 · BT26-004) · 배틀 에어리어의 이 옵션 (ST23-15 · ST24-15) · 시큐리티 맨 위 카드 (BT26-025) · 이 디지몬의 진화원 (P-152 · BT10-009)
  - 시큐리티: 이 디지몬 (BT26-022 · BT19-048 앞면) · 디지몬 1마리 (EX8-028 · EX9-056 · ST22-06 · BT18-034 · EX6-018 · LM-020 · BT25-044 · BT24-040 — 그 디지몬 주인의 시큐리티, AI는 상대 디지몬을 먼저) · 맨 위 카드 (EX13-032 · P-153 · BT9-044 · BT17-098 · BT20-084 · BT24-093) · 패의 카드 (EX5-054 · BT14-040 · BT15-042 · BT22-041 · EX5-031 · EX6-003 · EX6-068 · LM-023 · BT25-037) · 진화원의 카드 (P-146)
  - 그 밖: 다른 디지몬의 진화원 → 덱 아래 (BT11-070 · P-094) · 맨 위 카드 → 진화원 아래 (P-225) · 트래시 → 이 디지몬의 진화원 위 (EX10-021 · BT13-088 · EX9-057) · 패 · 트래시 → 다른 자신의 디지몬 아래 (EX7-051 · EX6-066 · BT8-104 · EX3-066 · EX11-053 · EX11-058 · BT21-071 · BT21-074 · BT25-083) · 자신의 디지몬 · 테이머 → 덱 아래 (P-024 · BT11-072) · "returning this Tamer to the hand" (BT15-082)
- 진화 조건 무시 진화 (약 25문장 — BT15-091 · BT17-053 · BT17-062 · BT19-088 · BT20-083 · BT23-070 · EX4-048 · EX4-066 · EX11-046 · ST14-02 · BT17-015/027 · BT22-013/026 등): "that Digimon may digivolve into [MetalGarurumon] in your hand, ignoring its digivolution requirements and without paying the cost" · "~ for a digivolution cost of 4, ignoring ~" · "ignoring its level" (BT12-089 · BT24-025 — Lv.만 무시, 색 조건은 본다) · 테이머의 "it may digivolve into ~" (EX13-074 — 이 테이머를 진화)
- 하이브리드체 [패] [메인] 테이머 진화 (BT18-026/053/070/081 · BT17-014/026 · BT12-015): "that Tamer digivolves into this card for digivolution cost of 3, ignoring its digivolution requirements" · "Digivolve it into this card as if that card is a level 4 red Digimon" — 테이머에서 진화와 같은 처리 (공식 Q&A BT4-011), [진화 시] · 진화했을 때 발휘
- 사건 (28문장): 테이머가 떠나려 할 때 — 엔진이 테이머를 소멸 · 패 · 덱 · 진화원으로 옮길 때도 알린다 ("When any of your Digimon or Tamers would leave the battle area by your opponent's effects" BT23-058 · BT24-101 · BT26-033 · BT25-039, 비용이면 막지 않는다) · "When another Digimon is returned to the hand" (BT14-030) · "When one of your Tamers is deleted or returned to the hand" (BT17-099) · "When any of your opponent's Digimon leave the battle area" (AD1-025) · "When an effect deletes one of your other Digimon or Tamers" (BT16-081) · "When effects delete Digimon" (BT21-100) · "When this Digimon checks your opponent's security stack" (BT16-033 · BT22-080) · "When an effect removes cards from your security stack" (BT15-084 — 체크는 아님) · "When a card is added to your opponent's security stack" · "When one of your Tamers digivolves" (BT7-081) · "When any of your Digimon digivolve from the trash" (BT23-068) · "When a red card returns from your trash to the hand" (BT16-011 · BT15-082) · "When a card is returned to the deck from your trash" (P-048) · "When an opponent's Digimon digivolves or attacks" (BT12-111) · 사건 안의 쉼표 목록 (BT11-003 · BT18-032 · EX6-074 · BT23-029) 등
- 조건 (215문장): "this Digimon's stack has 2 or more same-level cards" (BT22 · 공식 "이 디지몬에 Lv.이 같은 카드가 2장 이상 겹쳐져 있다면") · 진화원 안의 카드 ("a Tamer card with the [SoC] trait" · "a card with the [Hybrid] trait or [Takuya Kanbara]" · "a blue card" · "a non-red card" · "[Myotismon] or [X Antibody] was in ~") · "If not deleted in battle" · "If played by effects" · "If played or digivolved by effects" (AD1-024 — 사건의 디지몬) · "If played from digivolution cards" · "you have another (suspended / green / level 6) Digimon" · "you have fewer security cards than your opponent" · "your opponent has no Digimon with as many or more digivolution cards as this Digimon" · "this Tamer is suspended" · "If this effect didn't return / trash / flip" · "If you didn't" · "A or B" · "A and B" 두 조건 · 조건 안의 쉼표 목록 ("If this Digimon has the [Angel], [Archangel] or [Three Great Angels] trait") · 색 조건 무시의 조건 22문장 (자신 필드)
  - 결과 판단을 위해 "Return ~ to the hand / to the bottom of the deck" · 트래시 → 패 · 상대 시큐리티 파기 동작이 아무것도 하지 못하면 "하지 않았다"로 남긴다
  - "you have [Davis Motomiya] and [Ken Ichijoji] in play" (P-022): "and"로 이은 이름은 둘 다 있어야 한다 (전에는 어느 한쪽으로 읽었다 — 공식 한글 "「최산해」와 「서정우」가 있을 때")
- 키워드 부여: "1 of those Digimon gains ＜Rush＞ for the turn" (EX3-030 · EX3-031 · EX10-065 · ST19-14 · BT23-072 — 사건의 디지몬) · "The Digimon played by this effect gains ＜Rush＞" (EX5-033 · ST14-09 · P-219)
- 동작 표기: "Switch the target of attack to ~" (BT10-052 · BT11-070 · BT11-074 · BT11-092 · BT15-085 · BT16-045) · "Prevent this Digimon from leaving play" (BT11-062 · BT11-064) · "Trash both players' top security cards" · "Delete 1 of your opponent's Digimon and 1 of their Tamers" (BT18-101)
- 생성 한글 (공식 표기): "블루인 다른 자신의 디지몬 1마리를 이 디지몬의 진화원 아래에 놓는 것으로," · "이 디지몬을 특징으로 「Legend-Arms」를 가지거나 블랙인 자신의 다른 디지몬 1마리의 진화원 아래에 놓는 것으로," · "이 테이머와 자신의 트래시의 「가르고몬」과 「래피드몬」 1장씩을 자신의 「테리어몬」 1마리의 진화원 아래에 놓는 것으로," · "진화원을 갖지 않은 디지몬 1마리를 시큐리티 아래에 놓는 것으로," · "이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 자신의 시큐리티 위에 놓는 것으로," · "그 디지몬을 패의 「메탈가루몬」으로 진화 조건을 무시하고 코스트를 지불하지 않고 진화시킬 수 있다" · "그 테이머를 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다" · "이 디지몬에 Lv.이 같은 카드가 2장 이상 겹쳐져 있을 때," · "배틀 이외로 소멸하고 있었다면," · "진화원 매수가 이 디지몬 이상의 상대의 디지몬이 없을 때," · "특징으로 「수」/「짐승」을 포함(「수생」은 제외)하거나 특징으로 「로얄 나이츠」를 가진" 등
- 읽지 않고 남긴 것: 테이머를 디지몬으로 취급 (「최건우」 "is also treated as a 3000 DP Digimon" — 테이머가 어택 · 블록 · DP를 갖는 디지몬이 되는 모델이 엔진에 없음, 설계만) · 뒷면 진화원 (Ver.N — 진화원을 (카드, 뒷면) 쌍으로 바꾸는 큰 변경이라 설계만, 이번에 새로 놓는 뒷면 카드도 읽지 않음) · 고르는 효과의 머리말 ("for every 5 ~, activate 1 of the effects below") · 테이머에게 주는 효과 · 준 지속 효과 · "~ for each card placed by this effect" · "up to N ~ with different card numbers" · 효과로 등장/진화시킬 때의 코스트 감소 ("When you would play this card, by placing ~") · "your effects add to decks" · BT11-034 (영어는 두 번 놓고, 공식 한글은 "대신" — 엔진은 영어 원문대로)
- 테스트 49개 (EffectBatch33Tests), 경고 1개(BT5-086 내장 스크립트의 null 경고) 정리

## v0.32.0 — 2026-10-27
미지원 효과 문장 6차 묶음 — 도감 효과 문장 지원 7,740 → 7,916 / 10,366 (74.7% → 76.4%, +176)
- 모든 절이 동작하는 카드 2,322 → 2,424장 (수정 전 지원되던 문장 중 깨진 것 0 · 한글이 바뀌거나 사라진 것 0, 원래 지원하던 문장 중 한글이 새로 붙은 것 28문장). API 동기화로 새로 받은 카드 없음 (도감 4,526장 · 캐시 내용 그대로). 〈아츠 진화〉 미게재 12장(BT27 8장 · EX14-012 · LM-066~068)은 공식 카드 리스트(영어 · 일본어)에 여전히 없어 그대로 (추측하지 않음)
- "떠나려 할 때 · 소멸할 때"의 다른 표기 (39문장 — BT18-030 · BT19-029 · BT15-072 · BT17-036 · BT17-050 · BT17-057 · BT17-092 · BT17-100 · BT11-062 · BT9-012 · BT9-024 · EX3-013 · EX5-073 · EX6-030 · EX7-014 · BT20-100 · BT22-072 등)
  - "by an opponent's effect" · "by effects" · "other than by one of your effects or a battle" · "this yellow Digimon with ~" · 주어 안의 쉼표 목록 · "one of your [Apocalymon] or Digimon with the [Dark Masters] trait" · 원문 "would leave battle area" · "When this would leave"
  - "would be deleted or returned to your hand or deck (by an effect)" — 엔진이 떠나는 곳(소멸 · 패 · 덱 · 시큐리티)을 알린다. 공식 한글 (BT11-062): "효과로 소멸할 때, 또는 패/덱으로 되돌아갈 때" — 시큐리티로 가는 것은 아님
  - "When this Digimon has [Greymon] or [Omnimon] in its name and an effect would delete it ~" · "~ and would be deleted in battle" (BT9-012 · BT9-024 · P-072 · P-073) · "When an opponent's effect would delete this Digimon" (BT17-050)
  - 막기: "Prevent it" · "Prevent it from leaving play" · "Prevent its deletion" · "1 of those Digimon doesn't leave" · "prevent 1 of those Digimon from leaving"
  - 비용: "trashing 2 same-level cards from its digivolution cards" (공식 Q&A BT9-012 · BT9-024: 이 카드와 같은 Lv.이 아니라 진화원 중 서로 Lv.이 같은 2장 — 여러 Lv.이면 고른다) · "returning 4 [Vemmon] from its digivolution cards to the bottom of the deck" · "trashing 3 Digimon cards from its digivolution cards" · "trashing the bottom card of your security stack" · "returning your top security card to the bottom of the deck" · "trashing both players' top security cards"
- 효과를 준다 (따옴표 효과, 38문장 — BT15-068 · BT15-039 · BT11-103 · BT14-095 · BT20-065 · EX5-026 · EX6-048 · EX6-057 · EX8-012 · ST14-07 · BT6-104 · EX10-034 · BT25-054 · BT27-100 · EX12-016 · P-183 등): "1 of your opponent's Digimon gains "[On Deletion] Lose 1 memory." until ~" · "Give 1 of their Digimon "[Start of Your Main Phase] This Digimon attacks." until their turn ends" · "~ gets -3000 DP and gains "~"" · "Give ~ ＜Collision＞ and "~""
  - 받은 디지몬의 맨 위 카드 효과처럼 그 디지몬의 주인이 발휘 (기간 안, 진화해도 남는다). 공식 Q&A EX10-034: 효과를 받지 않는 디지몬에게도 줄 수 있지만 유발할 때 효과를 받지 않으면 유발하지 않는다. 시점 효과만 (지속 효과 · 키워드뿐인 것은 아직), 테이머에게 주는 것은 읽지 않음
  - 강제 어택 "This Digimon attacks." — 어택할 수 있으면 반드시 어택 (대상은 어택하는 쪽이 고른다 — 플레이어는 묻고 AI는 정책). 공식 Q&A EX5-048: 같은 시점에 여럿이어도 어택은 1번
  - 문장 나누기: 따옴표 안의 문장은 나누지 않고, 마침표로 끝나는 따옴표 뒤의 "Then, ~"는 다음 문장
- 사건 (약 30문장): 등장한 곳 "When you play a Digimon from your trash" · "~ from one of your Digimon's digivolution cards" · "When one of your Digimon is played from digivolution cards" (EX1-060 · BT4-087 · BT7-002 · BT7-028 · BT17-002 · BT17-082 · BT20-028 — 엔진이 효과로 등장시킬 때 있던 곳을 알린다) · 효과로 레스트 "When an effect suspends a Digimon or Tamer" · "When a Digimon is suspended by an effect" · "When one of your effects suspends a Tamer" · "When effects suspend any of your [WG] trait Digimon" (효과의 비용 포함, 어택 · 블록은 아님 — BT10-004 · BT14-004 · BT14-053 · BT15-063 · EX9-042) · "When any of your yellow or red Tamers suspend" · "When a digivolution card of an opponent's Digimon is trashed" (BT8-029 · BT12-026 · BT14-083) · "When any of your [TS] trait Digimon win a battle" · "When this Digimon with [Angoramon] in its text or the [NSp] trait wins a battle" · "When you play a 2-color black and yellow Digimon" · "~ with ＜Save＞ in its text" · "~ with the [Royal Knight] trait or a blue Tamer" · "When another [Eosmon] is played" · "When any of your Digimon or Tamers digivolve into Digimon with ~" · "When an opponent's Digimon with as many or fewer digivolution cards as this Digimon attacks" · "When this Digimon deletes an opponent's Digimon by battle" 등
- 테이머의 진화 코스트 감소 (BT2-088 · BT4-095 · BT5-092 · BT8-091 · BT9-090 · RB1-034 · BT8-112): "~, you may suspend this Tamer to reduce the memory cost of the digivolution by 1" · "into a Digimon card in your hand with [X] in its name" · "When digivolving one of your Digimon into ~" · 진화 카드의 쉼표 목록
- 두 동작을 한 문장에 쓴 것 (원래 규칙이 읽지 못할 때만, 약 20문장): "Suspend 1 of your opponent's Digimon, and 1 of your Digimon gains ＜Piercing＞ for the turn" · "Unsuspend this Digimon, and it gains ＜Blocker＞ ~" · "Suspend 1 Digimon, then unsuspend 1 Digimon" · "Suspend 2 of your opponent's Digimon. 2 of their Digimon or Tamers can't unsuspend ~" (EX3-070 · EX5-068 · BT15-053 · BT9-031 · P-109 · EX12-052 · ST17-12 등)
- 시큐리티 어택 셈 (BT10-057 · BT18-085 · BT6-029 · BT11-043): "For every 2 suspended Digimon you have in play, this Digimon gets +2000 DP and gains ＜Security A. +1＞" · "~ for each Digimon with no digivolution cards your opponent has in play" · "For each other Digimon with [Sukamon] in its name in play, ~"
- [육성] 두 문장 등장 코스트 감소 (디지타마도 [육성] 효과): BT13-007 "you may reduce the play cost by 4. Further reduce it by 1 for each of this Digimon's digivolution cards." — 공식 한글: "이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -1" (줄이는 양이 준다) · EX6-006 "If this Digimon has 5 or more cards with different names in its digivolution cards, you may reduce the play cost by 4 instead."
- 코스트 감소 조건 (12문장): "if you have a Digimon with [Huckmon], [Jesmon] or [Sistermon] in its name" · "~ with [X Antibody] in its digivolution cards (in play)" · "~ 2 or more suspended green Digimon in play" · "[Yuuko Kamishiro] or a [CS] trait Digimon" · "if your opponent has 3 or fewer security cards" · "~ 10 or more cards in either their hand or trash" · "reduce the memory cost of this card by 2" (BT9-095 · BT9-097 · BT10-103 · BT11-107 · BT20-057 · BT21-093 · BT23-044 · ST22-14 · BT9-110 · EX5-068)
- 덱 위 공개 (6문장): 공개한 카드로 진화 "You may digivolve this Digimon into a green level 6 card among them without paying its memory cost" (BT1-078 · BT7-049 · BT24-060 — 진화 조건은 보통 진화와 같다) · 옵션 사용 "You may use 1 [Three Musketeers] trait Option card among them without paying the cost" (EX7-048) · 원문 소문자 "trash the rest." (BT21-058 · EX13-028 · EX13-031 · BT11-105)
- 대상 표기: "with as much DP as this Digimon or less" · "with as much or less DP than this Digimon" · "1 of your opponent's play cost 7 or lower Digimon" · "with a play cost 5 or less" (BT17-017 · BT25-018 · BT25-093 · BT22-082 · BT9-062 · BT9-064 · P-205)
- 생성 한글 (공식 표기): 새로 지원한 176문장 모두 — "특징으로 「데이터종」/「윗체르니」를 가진 옐로인 이 디지몬이 상대의 효과로 배틀 에어리어를 벗어날 때," · "이 디지몬이 효과로 소멸할 때, 또는 패/덱으로 되돌아갈 때," · "~ 소멸하지 않고, 패/덱으로 되돌아가지 않는다" · "이 디지몬의 진화원에서 Lv.이 같은 카드 2장을 파기하는 것으로," · "상대의 턴 종료 시까지 상대의 디지몬 1마리에게 「【자신의 메인 페이즈 개시 시】 이 디지몬으로 어택한다.」의 효과를 준다" · "자신의 트래시에서 디지몬이 등장했을 때," · "디지몬이 효과로 레스트했을 때," · "블랙을 포함하는 2색의 디지몬 카드로 진화할 때," · "이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -1." · "그중 특징으로 「3총사」를 가진 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다." 등
- 읽지 않고 남긴 것: 테이머를 디지몬으로 취급 (「최건우」 "is also treated as a 3000 DP Digimon" — 엔진에 테이머가 디지몬이 되는 모델이 없음) · 뒷면 진화원 (Ver.N "face-down digivolution cards") · 테이머가 떠나려 할 때 ("your Digimon or Tamers would leave") · 테이머에게 주는 효과 · 준 지속 효과 · 고르는 효과의 머리말 ("for every 5 of ~, activate 1 of the effects below") · [육성]의 시점 · [메인] 효과 (엔진이 육성 에어리어 디지몬의 효과를 알리지 않음) · "reduce the play cost by the deleted Digimon's play cost"
- 테스트 44개 (EffectBatch32Tests)

## v0.31.0 — 2026-10-26
미지원 효과 문장 5차 묶음 — 도감 효과 문장 지원 7,548 → 7,740 / 10,366 (72.8% → 74.7%, +192)
- 모든 절이 동작하는 카드 2,217 → 2,322장 (수정 전 지원되던 문장 중 깨진 것 0 · 한글이 사라진 것 0). API 동기화로 새로 받은 카드 없음 (도감 4,526장 그대로). 〈아츠 진화〉 미게재 12장(BT27 8장 · EX14-012 · LM-066~068)은 공식 카드 리스트(영어 · 일본어)에 아직 없어 그대로 (추측하지 않음)
- 다른 카드의 등장 코스트 감소 (필드 · 테이머 · [육성], 14문장 — ST20-12 · ST20-13 · ST21-12 · ST21-13 · P-199 · BT22-094 · BT14-046 · BT16-089 · EX2-057 · EX2-007 · EX3-040 · BT22-079 · BT22-080 · BT23-073): "[Your Turn] When any Digimon cards with the [ADVENTURE] trait would be played from your hand, by suspending this Tamer, reduce the play cost by 1" · "When you would play [MarineAngemon] from your hand, reduce its play cost by 1" · "[Breeding] ~ you may reduce the play costs by 1" · 비용 "by deleting this Tamer"
  - 공식 Q&A ST20-12 · ST21-12: 여러 장의 효과를 각각 발휘하면 더한다. 패에서 등장할 때 (옵션 · 듀얼 카드 사용은 아님), [육성]은 육성 에어리어의 디지몬(진화원 포함)
- 진화 코스트 감소: "~ by 1 for each green or black Tamer you have in play" (BT11-059) · "~ for each color in your opponent's trash" (BT18-085) · "When a Digimon with [X Antibody] in its digivolution cards would digivolve into this card" (BT10-086) · "When one of your [Paildramon] or [Dinobeemon] would digivolve ~" (BT3-111) · "When you would digivolve this Digimon into an [AncientGreymon] in your hand" (P-029 · P-030 — 원문 정리 규칙이 "would digivolve"를 깨뜨리던 것을 고침) · BT21-020 · EX4-063
- 배틀 에어리어 디지몬의 【카운터】 (종합 룰 11-3 · 15-16-14: 어택받는 쪽이 어택 1번에 1개, EX12-017 · EX12-055 · EX12-057 · EX13-015): 상대의 디지몬이 어택했을 때 (≪블래스트 진화≫를 쓰지 않았으면) 발휘할지 묻는다. 디지몬이 아닌 카드 · [패]의 【카운터】는 읽지 않음
- [트래시] 시점 효과 (트래시의 이 카드, BT12-110 · BT23-097 · BT24-096 · EX7-072 · EX8-072): "[Trash] [Your Turn] When any of your Digimon digivolve into [Belphemon (X Antibody)], by returning this card to the bottom of the deck, activate this card's [Main] effects" — 엔진이 트래시에도 사건 · 턴 시점을 알린다
  - 카드명 안의 괄호 "[Beelzemon (X Antibody)]" · "[Sistermon Blanc (Awakened)]"를 설명문으로 지워 「베르제브몬」과 같게 보던 것을 고침 (EX11-071 · BT7-082 · BT7-083의 이름 조건도 정확해짐)
- 테이머 시점: "When this Tamer becomes suspended / unsuspends" · "When one of your (red or yellow) Tamers is deleted" (BT12-042 · BT13-015 · BT17-037 · BT21-044) · "When effects trash cards from under your Tamers / this Tamer" (ST23-13 · ST23-14 · ST24-13 · ST24-14 · BT26-002 · BT26-057 · BT25-029) · "When other Digimon or Tamers are deleted" (BT19-075 · EX10-011) — 효과 · 비용으로 테이머를 소멸시키면 알린다
- 링크 카드: "When effects trash any of this Digimon's / your Digimon's link cards" (EX10-001 · EX10-030 · EX10-043 · EX10-062 · EX10-073) · "When any of your Digimon get linked to a [Social], [Creation], [Navi] or [Tool] trait card" (P-217 · P-218 · P-233)
- 패 · 덱: "When a Digimon card with the [Dark Animal] or [SoC] trait is trashed from your hand" · "When a card is trashed from your hand (by your effect)" · "When hands are trashed from" · "When you trash this card in your hand using one of your effects" (BT6-108 · BT7-076 · BT7-077 · BT14-100) · "When your opponent's deck is trashed from" · "When effects add cards to your opponent's hand"
- 어택 · 진화 · 등장 · 소멸: "When you attack with a Digimon that has [Angoramon] in its name or is level 5 or higher" (BT10-091 · BT8-086 · BT9-086 · EX2-061 · EX2-062) · "When one of your Digimon with [Gammamon] in its digivolution cards attacks" (P-062 · P-063 · P-064) · "~ with 2 or more colors attacks" (BT8-085 · BT8-089) · "When an opponent's Digimon digivolves (into a level 6 or higher Digimon)" · "When any Digimon are played or digivolve" · "When an opponent plays a Digimon (or Tamer)" · "When an opponent's Digimon is deleted in battle or by having 0 DP" · "When effects digivolve your opponent's Digimon" 등
  - "A or B" 사건 ("When attack targets change or effects trash cards from under your Tamers") — 어느 쪽 사건이든. 사건 안의 쉼표 목록 ("[Social], [Creation], [Navi] or [Tool] trait")도 읽는다
- 합계로 고르는 소멸 (20문장 — ST7-12 · BT9-094 · BT8-105 · BT14-068 · BT17-018 · EX2-011 · AD1-008 · EX14-012 · LM-021 · EX4-049 등): "Choose any number of your opponent's Digimon whose total DP adds up to 6000 or less and delete them" · "Delete up to 7 play cost's total worth of ~" · "~ return them to the bottom of their owner's deck" · 상한 늘리기 "add 3000 to the maximum you can choose with this effect" — 고를 때마다 남은 합계 안에서 1마리씩 (AI는 큰 것부터)
- "activate 1 of this Digimon's [When Digivolving] effects" (13문장 — BT20-035 · BT20-080 · BT20-081 · EX6-043 · BT22-040 · BT22-042 · BT24-079 · BT16-102 · EX3-026 · EX8-074 · EX12-064 · EX13-060 · BT11-112 "that Digimon's")
- 진화원 카드의 효과 전부를 얻는다 (16문장 — BT10-011 · BT15-039 · BT16-062 · RB1-009 · RB1-015 · RB1-023 · RB1-030 · BT12-072 · BT16-014): 맨 위 카드 · 진화원 효과로, 조건에 맞는 진화원 카드의 시점 효과 · 지속 효과 · 키워드 · [메인]을 이 디지몬이 얻는다. 공식 Q&A BT16-062 · BT16-014: 진화할 때 얻은 【진화 시】도 발휘
- 그 밖: "Both players draw 1 card from their decks" · "Both players ＜Draw 1＞" · "Place the top 2 cards of your deck face down under this Tamer" · 꺾쇠 없는 "Draw 1" 원문 · "cards in hand" · "Place 1 [Marcus Damon] from your trash as the top security card"
- 버그 수정: 테이머 · 배틀 에어리어의 옵션 · 트래시 카드의 [턴에 1회] · [턴에 2회]가 지켜지지 않던 것 (디지몬만 기록하고 있었다 — 테이머의 "[Your Turn] [Once Per Turn] When ~"가 매번 발휘)
- 생성 한글 (공식 표기): 새로 지원한 192문장 모두 — "자신의 패에서 특징으로 「어드벤처」를 가진 디지몬 카드가 등장할 때, 이 테이머를 레스트시키는 것으로, 지불하는 등장 코스트를 -1 한다" · "그린/블랙인 자신의 테이머 1명마다" · "이 테이머가 레스트했을 때," · "레드/옐로인 자신의 테이머가 소멸했을 때," · "이 디지몬의 링크 카드가 효과로 파기되었을 때," · "자신의 패가 자신의 효과로 파기되었을 때," · "명칭에 「앙고라몬」을 포함하거나, Lv.5 이상인 자신의 디지몬이 어택했을 때," · "상대의 디지몬이 등장/진화했을 때," · "다른 디지몬/테이머가 소멸했을 때," · "DP의 합계가 8000 이하가 되도록 상대의 디지몬을 골라, 고른 디지몬 전부를 소멸시킨다" · "등장 코스트 합계 7까지 상대의 디지몬을 소멸시킨다" · "이 디지몬의 【진화 시】 효과 1개를 발휘한다" · "서로는 덱에서 카드를 1장 뽑는다" · "이 디지몬은 이 디지몬의 진화원에 있는 명칭에 「감마몬」을 포함하는 카드의 효과 전부를 얻는다" · "「베르제브몬 X항체」" 등. 원래 지원하던 문장 중 한글이 새로 붙은 것 8문장, 공식 표기에 맞게 바뀐 것 10문장 (BT11-055 · BT11-059 "그린/블랙인 자신의 테이머 1명마다" · X항체 · 각성 이름)
- 읽지 않고 남긴 것: 뒷면 진화원 (BT22-038 · BT22-061 · EX9-031/041/063 — 엔진에 앞뒷면 진화원이 없음) · 효과로 코스트를 지불하고 등장시킬 때의 다른 카드 코스트 감소 (패에서 등장할 때만) · 테이머 아래 카드는 테이머마다가 아니라 한 묶음 ("under this Tamer"는 자신의 테이머 아래 전체로 봄) · [트래시]의 "would be deleted" 시점 (BT17-065/067/073) · "this Digimon's [When Digivolving] effects"에 진화원 효과 포함 여부 (맨 위 카드 · 얻은 효과만) · 진화원 카드의 효과 얻기에서 코스트 감소 · 특수 진화 등 규칙 효과는 제외
- 테스트 47개 (EffectBatch31Tests)

## v0.30.0 — 2026-10-26
미지원 효과 문장 4차 묶음 — 도감 효과 문장 지원 7,404 → 7,548 / 10,366 (71.4% → 72.8%, +144)
- 모든 절이 동작하는 카드 2,140 → 2,217장 (수정 전 지원되던 문장 중 깨진 것 0 · 한글이 사라진 것 0). API 동기화로 새로 받은 카드 없음 (도감 4,526장 그대로)
- 듀얼 카드 〈아츠 진화〉 (종합 룰 2-11 · 4-20, 공식 Q&A Q6313): 옵션 쪽으로 사용한 뒤 트래시하는 대신, 에어리어의 자신의 디지몬을 그 카드로 코스트 없이 진화 (임의, 진화 조건은 보통 진화와 같다) → 1장 드로우 · 그 카드의 [진화 시]
  - 영어 API 데이터에는 없는 아이콘이라, 공식 카드 리스트(world.digimoncard.com · digimoncard.com)의 [DUAL Rule] 칸을 로컬 파일 `data/kr/dual_rules.json`(개인 사용 · git 제외)으로 받아 읽는다 (`DualCardRules`). 확인된 19장(BT25 · BT26 · EX12 · EX13 · ST23 · ST24)은 모두 〈아츠 진화〉. 공식 사이트에 아직 없는 12장(BT27 8장 · EX14-012 · LM-066~068)은 추측하지 않고 트래시
  - 효과 처리 중에 유발한 [소멸 시] 등은 아츠 진화 뒤에 발휘 (Q6313). 화면: 듀얼 카드의 옵션 쪽 설명 끝에 "〈아츠 진화〉 (사용한 뒤 트래시하는 대신, 자신의 카드를 이 카드로 코스트를 지불하지 않고 진화시킬 수 있다)"
- 진화원을 비용으로 옵션 쪽 [메인] (BT27-043 켄타루몬 X · BT27-070 크레니엄몬 X · BT27-084 로드나이트몬 X): 비용 "by placing 1 [Kentaurosmon] or [X Antibody] from this Digimon's digivolution cards as the bottom security card" · "by trashing 1 [Craniamon] or [X Antibody] from this Digimon's digivolution cards" · 시점 "When any of your other Digimon are played or deleted" · 옵션 쪽 "For each card in both players' security stacks, add 1000 to this effect's DP reduction" · "it further gets -5000 DP" (BT26-031)
- 이 효과로 등장한 디지몬 (BT23-017 · BT23-037 · BT23-048 · EX4-063 · EX10-072 · EX11-022 · EX11-061 · P-216): "The Digimon this effect played can't digivolve and is deleted at the end of your opponent's turn" · "~ is deleted at turn end" · "At turn end, delete the Digimon this effect played" · "At the end of your turn, ~" — 그 효과(절)가 직접 등장시킨 디지몬만, 정해진 턴 종료 시에 그 효과의 주인이 소멸시킨다
- 배틀 에어리어의 옵션 카드 (공식 Q&A BT19-062 · BT23-059: 「배틀 에어리어에 놓는다」로 놓인 옵션): "When this card is trashed from the battle area" · "When effects trash this card from the battle area" (≪딜레이≫ · 비용 · 효과 모두) · "When Option cards in the battle area are trashed" · "When any of your [CS] trait Option cards are placed in the battle area" · 비용 "by trashing 1 (of your) (non-red) Option card(s) in the battle area" · "by placing 1 Option card with the [Device] trait from your hand in the battle area" · 동작 "Trash 1 of your / their Option cards in the battle area" · "Place 1 [Device] trait Option card ~ from your trash / hand in the battle area" · 디바이스 "While you don't have [Queen Device] (in the battle area), you may ignore this card's color requirements" (BT19 · P-155 · P-159 · P-161 · EX3 · EX8-052 · BT17-100 등)
- 비용 "by deleting 1 of your opponent's ~" (BT24-018 · EX13-015): 공식 Q&A EX13-015 (Q7247) · BT24-018 (Q5599) — 상대 디지몬의 소멸이 막히면 "~하는 것으로"를 채우지 못해 뒤는 처리하지 않는다 (배틀 에어리어를 벗어난다)
- "When one of your Digimon's effects adds cards to your hand" (BT15-002 · BT15-026 · BT15-083): 자신의 디지몬(배틀 에어리어의 메인 · 진화원 효과, [소멸 시] 포함)의 효과로 패가 늘었을 때. 공식 Q&A BT15-002: 1장 드로우하고 1장 버려 장수가 같아도 발휘 — "효과로 패가 늘어났을 때"(v0.29.0)도 같은 판단으로 고침 (드로우를 센다)
- [타이탄족] 트래시 진화 (BT24-009 · BT24-013 · BT24-021 · BT24-026 · BT24-042 · BT24-045 · BT26-066 · BT26-069): "When your hand is trashed from, this [Demon] or [Titan] trait Digimon may digivolve into [Titamon] or a [Titan] trait Digimon card in the trash with the (digivolution) cost reduced by 1" — 진화 동작이 "with the cost reduced by N"도 읽어 EX12 · EX13 · BT25 · BT26 · ST23 · ST24 등의 같은 표기도 함께
- 테이머에서 진화 (하이브리드체, 28문장 — BT4 · BT6 · BT7 · BT12 · BT17): "You may digivolve this card from your hand onto one of your red Tamers (or [Takuya Kanbara]s) as if the Tamer is a level 3 red Digimon (for a memory cost of 2)"
  - 공식 Q&A BT4-011: 테이머를 디지몬으로도 취급하여 진화 — "디지몬이 진화할 때 · 했을 때"가 유발, 1장 드로우 · [진화 시]. 테이머가 진화원 맨 아래, 레스트 상태 그대로, 테이머가 1명이면 테이머 아래 카드도 진화원으로 (종합 룰 4-3-2)
  - 종합 룰 3-4-5 (겹쳐지면 같은 존재): 이번 턴에 나온 테이머가 아니면 진화한 디지몬은 바로 어택할 수 있다 (엔진이 테이머가 나온 턴을 적어 둔다)
  - 화면: 손패의 하이브리드체를 내 테이머 카드 위에 놓으면 "🧬 테이머에서 진화" (등장도 가능하면 고르기). AI: 어택 전에 메모리 안에서 한다
- "add N to this effect's level / play cost / DP maximum" (BT16-079 · BT18-029 · BT19-052 · BT21-078 · BT21-079 · BT23-057 · EX8-028 · EX10-049 · ST21-11): 앞 문장의 상한을 셈 · 조건만큼 늘려 처리 (공식 BT18-029 "다른 자신의 디지몬 1마리마다 이 효과의 Lv. 상한 +1"). 셈 "colors your Tamers have" · "cards in both players' trashes / security stacks", 조건 "If your Tamers have 2 or more total colors"
- 비용 안의 쉼표 목록 "By trashing 1 card with the [Demon], [Shaman] or [Titan] trait from your hand, ~" (BT11-063 · BT15-007 · BT22-089 · BT24-008 · P-242 · P-247 등 15문장) · 원문 오타 "By trash 1 card" · 비용 "trashing 2 level 5 Digimon cards in this Digimon's digivolution cards to prevent ~" (EX1-073) · "trashing 1 Option card in this Digimon's digivolution cards" (EX7-048 · EX7-059 · EX7-073)
- 생성 한글 (공식 표기): 새로 지원한 144문장 중 142문장 — "이 효과로 등장한 디지몬은 진화할 수 없으며, 상대의 턴 종료 시, 소멸한다" · "배틀 에어리어의 자신의 옵션 카드 1장을 파기한다" · "이 카드가 배틀 에어리어에서 파기되었을 때," · "자신의 패가 자신의 디지몬의 효과로 늘어났을 때," · "패의 이 카드는, 레드인 자신의 테이머를 레드인 Lv.3의 디지몬으로서 취급하여 진화할 수 있다." · "다른 자신의 디지몬 1마리마다 이 효과의 Lv. 상한 +1" · "이 디지몬의 진화원에서 「크레니엄몬」/「X항체」 1장을 파기하는 것으로," 등. 나머지 2문장(BT23-017 · BT23-055)은 공식 한글 데이터가 화면에 나온다. 원래 지원하던 문장 중 한글이 새로 붙은 것 43문장
- 읽지 않고 남긴 것: 〈아츠 진화〉 데이터가 공식 사이트에 없는 12장 · 듀얼 카드 옵션 쪽 중 API 원문이 잘린 문장 (BT25-057 "gains , and" · BT26-033 · BT26-056 "Then," · EX13-065/066 "card with from" 등 — 영어 API 데이터만 쓰므로 고치지 않음) · 엔진이 효과 안에서 바로 처리하는 [등장 시] · [어택 시]는 아츠 진화로 잃지 않는다 (공식 Q&A Q7430 · Q6344: 아츠 진화로 진화하면 발휘하지 못함 — 효과 대기열 구조가 달라 보류) · "효과로 등장한 디지몬"의 소멸이 막혔을 때 진화 불가가 이어지는지
- 테스트 39개 (EffectBatch30Tests)

## v0.29.0 — 2026-10-26
미지원 효과 문장 3차 묶음 — 도감 효과 문장 지원 7,279 → 7,404 / 10,366 (70.2% → 71.4%, +125)
- 모든 절이 동작하는 카드 2,100 → 2,140장 (수정 전 지원되던 문장 중 깨진 것 0 · 한글이 사라진 것 0). API 동기화로 새로 받은 카드 없음 (도감 4,526장 그대로)
- 진화원의 【메인】 (공식 Q&A EX5-007 · EX5-016): "[Main] [Once Per Turn] By placing this [CS] trait Digimon's top stacked card as its bottom digivolution card, ~" · "By placing the top card of this Digimon with the [Light Fang] or [Night Claw] trait as this Digimon's bottom digivolution card, gain 2 memory" · "By placing this Digimon's top stacked card as its bottom digivolution card, ~" (BT22-043 · BT22-044 · BT22-054 · BT22-069 · BT23-008 · BT23-018 · EX5-007 · EX5-016)
  - 엔진: `GetMainAbilities`가 배틀 에어리어 디지몬의 진화원 카드 [메인]도 돌려준다 (`MainAbility.Inherited`). 디지몬 · 진화원 카드마다 턴에 1번, 육성 에어리어는 아님. 조건 · 비용이 안 맞으면 Fail(쓴 것으로 치지 않음)
  - 맨 위 카드를 진화원 맨 아래로 놓는 것은 "효과로 진화원에 카드가 놓였다" (BT22-043 메인 효과와 이어진다). 같은 카드의 메인 효과와 진화원 효과의 [턴에 1회]를 따로 센다
  - 화면: 필드 디지몬 우클릭 = [메인] 효과 (맨 위 카드 · 진화원 중 여러 개면 고르기), 쓸 수 있으면 마우스 오버에 "우클릭: [메인] 효과 쓰기 — 진화원 [이름]"
  - AI: 맨 위 카드를 내리는 비용은 어택을 마쳐 레스트 상태인 디지몬에서만 쓴다
- 듀얼 카드 (종합 룰 4-6 · 2-7, API type "Dual" 31장): 지금까지 "진화원 [메인]"으로 보이던 BT25~BT27 · EX12~EX14 · LM · ST23 · ST24 카드의 아래 칸은 진화원 효과가 아니라 **옵션 쪽**
  - 카드 데이터에 `IsDual` (영어 API의 카드 종류). 진화원이 되어도 옵션 쪽은 진화원 효과가 아니다
  - 패에서: 내 디지몬 위에 놓으면 디지몬 쪽으로 진화, 배틀 에어리어 빈 곳에 놓으면 옵션 쪽으로 사용 (등장하지 않는다). 사용 코스트 = 카드의 코스트, 색 조건 또는 "Use Requirement: ~ trait" (≪사용 조건≫ — 공식 공지 2026-10-16), 쓰고 나면 트래시
  - 디지몬 쪽의 "Activate 1 [Main] effect on this card's Option side" (BT25-104 · BT27-035 · BT27-082 · LM-068)
  - 사용 코스트 증감: "For each of your security cards, add 1 to this card's use cost" (BT26-033 · BT26-097) · "When this card would be used, by ~, reduce the (use) cost by 2" (LM-066 · BT25-096 · BT26-098 · BT27-101) · "~ if ~, reduce the use cost by 3" (P-219)
  - AI: 듀얼 카드를 디지몬으로 등장시키지 않고, 옵션 쪽 문장으로 사용을 판단한다
- ≪오버클럭≫ (종합 룰 16-34, 12장): 자신의 턴 종료 시, 토큰 또는 지정된 다른 자신의 디지몬 1마리를 소멸시키는 것으로(임의) 레스트하지 않고 플레이어에게 어택 — "([Puppet] Trait)" · "([Chuumon]/[Sukamon] in name)". AI는 토큰이면 쓰고, 지정 디지몬이면 훨씬 약하고 상대 시큐리티가 적을 때만
- 토큰 또는 [퍼펫형]: "by deleting 1 of your Tokens or other [Puppet] trait Digimon" (비용) · "When any of your Tokens or other [Puppet] trait Digimon are deleted" · "When any of your Tokens or [Puppet] trait Digimon are played" (EX7-027 · EX9-032 · ST19-11 · BT22-002 · BT22-036 · BT22-088 · EX7-063 · EX11-022)
- ≪급습≫ (Engage, 16-44, 8장): 자신의 턴 종료 시 보통 어택 (레스트 · 등장한 턴은 ≪속공≫ 필요) · ≪천승≫ (Ascension, 16-43, 7장): 소멸했을 때 이 카드를 시큐리티 맨 위에 (임의)
- 진화할 수 없다: "[All Turns] This Digimon can't digivolve" (BT13-080 · BT27-075 등 6장) · "1 of your opponent's Digimon (or Tamers) can't digivolve until their turn ends" (BT19-073 · BT22-062 · BT25-072 · BT25-074 · EX11-045 · EX13-034) — 패 · 효과 · DNA 진화 모두 막는다 (공식 Q&A BT13-080: 효과로 진화원에 넣는 것은 진화가 아니므로 된다)
- 이 카드가 시큐리티 · 덱 · 패에서 파기되었을 때 (트래시의 이 카드로)
  - "When an effect trashes this card from the security stack, ~" · "activate this card's [Main] / [Security] effects" · "you may play this card" (BT13-098 · BT13-106 · BT15-037 · BT15-084 · BT15-092 · BT18-098 · BT22-034 · BT27-099 · ST22-10 등) — 공식 Q&A BT13-098: 체크 · 오픈 뒤 파기는 아니고 직접 파기만 (≪방벽≫ 포함)
  - "When this card is trashed from your deck, ~" (P-077 · EX2-071 · EX2-074 · EX10-037 · BT10-108 · BT19-097 · ST14-12) — 공식 Q&A P-077: 공개 · 확인 중인 카드는 아님
  - "When this card is trashed from your hand by one of your effects, ~" (BT10-080, Q&A)
- "When an effect adds a card to your hand" (BT7-029 · BT9-002 · BT9-021 · BT17-083): 효과로 드로우 · 패에 추가 (종합 룰 4-14-1 "드로우 = 덱에서 뽑아 패에 더하는 것") — 효과 하나가 끝난 뒤 1번
- 생성 한글 (공식 표기): 새로 지원한 125문장 중 122문장 — "특징으로 「CS」를 가진 이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 이 디지몬의 진화원 아래에 놓는 것으로," · "이 카드가 시큐리티에서 효과로 파기되었을 때," · "이 카드가 덱에서 파기되었을 때," · "효과로 자신의 패가 늘어났을 때," · "이 디지몬은 진화할 수 없다" · "자신의 토큰 또는 특징으로 「퍼펫형」을 가진 다른 자신의 디지몬 1마리" · "이 카드의 옵션 쪽의 【메인】 효과를 1개 발휘한다" 등. 나머지 3문장(BT10-080 · BT23-008 · BT23-018)은 공식 한글 데이터가 화면에 나온다
  - 키워드 이름을 종합 룰 Ver.4.3대로: 《인게이지》 → 《급습》 · 《어센션》 → 《천승》 · 《가드》 → 《수호》 · 《디태치》 → 《분리》, ≪오버클럭≫ 설명 "특징으로 「퍼펫형」을 가진 다른 자신의 디지몬"
- 읽지 않고 남긴 것 (공식 근거 부족): 듀얼 카드의 〈아츠 진화〉(카드에 아이콘이 있는 카드만 — API 데이터에 없음) · 효과로 "등장"시킬 때 듀얼 카드를 디지몬 카드로 볼지 · ≪오버클럭≫ 어택이 등장한 턴에도 되는지 · "When one of your Digimon's effects adds cards to your hand"(효과의 주인 종류) · 비용 "by deleting 1 of your opponent's ~"
- 테스트 32개 (EffectBatch29Tests)

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
