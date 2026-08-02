using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 파이브 카드 드로우의 전체 진행 단계를 나타냅니다.
/// </summary>
public enum GamePhase
{
    Preparing = 0,
    FirstBetting = 1,
    Exchange = 2,
    FinalBetting = 3,
    Showdown = 4
}

/// <summary>
/// 플레이어가 선택할 수 있는 베팅 행동입니다.
/// </summary>
public enum BettingAction
{
    Fold = 0,       // 다이
    Ping = 1,       // 삥
    Double = 2,     // 따당
    Call = 3,       // 콜
    Check = 4,      // 체크
    Quarter = 5,    // 쿼터
    Half = 6,       // 하프
    AllIn = 7,      // 올인
    Max = 8         // 맥스
}

/// <summary>
/// Player 0이 교환 페이즈에서 선택할 수 있는 행동입니다.
/// </summary>
public enum ExchangeAction
{
    Pass = 0,       // 카드 교환 없이 진행
    Exchange = 1    // 선택한 카드 교환
}

/// <summary>
/// 베팅 행동별 아이콘 스프라이트 묶음입니다.
/// 게임매니저 인스펙터에서 다이, 삥, 따당, 콜, 체크, 쿼터, 하프, 올인, 맥스를 각각 연결합니다.
/// </summary>
[Serializable]
public class BettingActionSpriteSet
{
    [Tooltip("다이 행동 아이콘입니다.")]
    public Sprite foldSprite;

    [Tooltip("삥 행동 아이콘입니다.")]
    public Sprite pingSprite;

    [Tooltip("따당 행동 아이콘입니다.")]
    public Sprite doubleSprite;

    [Tooltip("콜 행동 아이콘입니다.")]
    public Sprite callSprite;

    [Tooltip("체크 행동 아이콘입니다.")]
    public Sprite checkSprite;

    [Tooltip("쿼터 행동 아이콘입니다.")]
    public Sprite quarterSprite;

    [Tooltip("하프 행동 아이콘입니다.")]
    public Sprite halfSprite;

    [Tooltip("올인 행동 아이콘입니다.")]
    public Sprite allInSprite;

    [Tooltip("맥스 행동 아이콘입니다.")]
    public Sprite maxSprite;

    public Sprite GetSprite(BettingAction action)
    {
        switch (action)
        {
            case BettingAction.Fold:
                return foldSprite;

            case BettingAction.Ping:
                return pingSprite;

            case BettingAction.Double:
                return doubleSprite;

            case BettingAction.Call:
                return callSprite;

            case BettingAction.Check:
                return checkSprite;

            case BettingAction.Quarter:
                return quarterSprite;

            case BettingAction.Half:
                return halfSprite;

            case BettingAction.AllIn:
                return allInSprite;

            case BettingAction.Max:
                return maxSprite;

            default:
                return null;
        }
    }
}

/// <summary>
/// 각 베팅 토글 아래에 배치한 금액 박스와 금액 Text 참조입니다.
/// Amount Text를 비워두면 Box Object 아래의 첫 번째 Text를 자동으로 찾습니다.
/// </summary>
[Serializable]
public class BettingAmountUIReference
{
    [Tooltip("베팅 금액을 표시할 박스 오브젝트입니다.")]
    public GameObject boxObject;

    [Tooltip("금액 문자열을 표시할 Text입니다. 비워두면 박스 하위에서 자동으로 찾습니다.")]
    public Text amountText;

    public Text ResolveText()
    {
        if (amountText == null && boxObject != null)
        {
            amountText =
                boxObject.GetComponentInChildren<Text>(true);
        }

        return amountText;
    }
}

/// <summary>
/// 5인용 파이브 카드 드로우 게임 진행을 관리합니다.
///
/// 주요 기능:
/// - 매 판 딜러, SB, BB 로테이션
/// - 카드 52장 셔플 및 플레이어당 5장 분배
/// - 첫 베팅 → 교환 → 마지막 베팅 → 쇼다운
/// - 다이, 삥, 따당, 콜, 체크, 쿼터, 하프, 올인, 맥스 처리
/// - 족보 판정과 메인팟/사이드팟 지급
/// - 다음 게임 시작 시 카드와 이전 판 상태 초기화
/// - 보유 금액이 0인 플레이어에게 시작 금액 재지급
/// </summary>
public class FiveCardDrawGameManager : MonoBehaviour
{
    [Header("Players")]
    [Tooltip("Player Number가 0~4인 플레이어 5명을 등록합니다.")]
    public List<PlayerControl> players = new List<PlayerControl>();

    [Header("Blind Rule")]
    public long smallBlindAmount = 100L;
    public long bigBlindAmount = 200L;

    [Header("Betting Rule")]
    [Tooltip("베팅 금액을 맞추는 최소 단위입니다.")]
    public long bettingUnit = 100L;

    [Tooltip("삥 금액입니다. 0이면 BB 금액을 사용합니다.")]
    public long pingBetAmount = 0L;

    [Tooltip("한 플레이어가 한 판 전체에서 블라인드와 모든 베팅 라운드를 합쳐 낼 수 있는 최대 금액입니다. 0이면 제한이 없습니다.")]
    public long maxBetAmountPerGame = 4_000_000L;

    [Header("Exchange Rule")]
    [Range(0, 5)]
    public int maxExchangeCards = 3;

    [Header("Automatic Exchange Recommendation")]
    [Tooltip("Player 0이 최초 카드 5장을 받은 뒤 추천 교환 카드를 자동으로 선택합니다.")]
    public bool autoSelectRecommendedExchangeCards = true;

    [Header("Card Visual Resources")]
    [Tooltip("카드 번호 0~51 순서대로 앞면 스프라이트를 등록합니다.")]
    public Sprite[] cardSprites =
        new Sprite[CardUtility.TotalCardCount];

    [Tooltip("AI 손패와 사용하지 않은 카드 더미에 표시할 카드 뒷면입니다.")]
    public Sprite cardBackSprite;

    [Tooltip("교환되어 사용한 카드 더미에 표시할 반환용 카드 뒷면입니다.")]
    public Sprite cardReturnBackSprite;

    [Tooltip("아직 분배되지 않은 카드가 모이는 위치입니다.")]
    public RectTransform unusedCardDeckPosition;

    [Tooltip("교환으로 버린 카드가 모이는 위치입니다.")]
    public RectTransform discardedCardPosition;

    [Header("Game Flow Delay")]
    [Tooltip("새 게임 초기화가 끝난 뒤 SB 블라인드를 수거하기 전 대기 시간입니다.")]
    [Min(0f)]
    [FormerlySerializedAs("cardResetDelay")]
    public float gameStartBeforeBlindDelay = 0.45f;

    [Tooltip("SB 블라인드를 수거한 뒤 BB 블라인드를 수거하기까지의 간격입니다.")]
    [Min(0f)]
    public float blindPostInterval = 0.25f;

    [Tooltip("SB와 BB 블라인드 수거가 모두 끝난 뒤 최초 카드 분배를 시작하기 전 대기 시간입니다.")]
    [Min(0f)]
    public float blindsToInitialDealDelay = 0.65f;

    [Tooltip("최초 카드 25장 분배와 손패 정렬이 끝난 뒤 첫 번째 베팅을 시작하기 전 대기 시간입니다.")]
    [Min(0f)]
    public float initialDealToFirstBettingDelay = 0.55f;

    [Tooltip("첫 번째 베팅이 끝난 뒤 카드 교환 페이즈를 시작하기 전 대기 시간입니다.")]
    [Min(0f)]
    public float firstBettingToExchangeDelay = 0.75f;

    [Tooltip("모든 플레이어의 카드 교환이 끝난 뒤 마지막 베팅을 시작하기 전 대기 시간입니다.")]
    [Min(0f)]
    public float exchangeToFinalBettingDelay = 0.75f;

    [Tooltip("마지막 베팅이 끝난 뒤 쇼다운을 시작하기 전 대기 시간입니다.")]
    [Min(0f)]
    public float finalBettingToShowdownDelay = 0.75f;

    [Tooltip("게임 종료 상태가 된 뒤 족보 판정과 승자 표시를 보여주기 전 대기 시간입니다.")]
    [Min(0f)]
    public float gameEndToWinnerDisplayDelay = 1f;

    [Header("Card Animation Timing")]
    [Tooltip("처음 카드를 한 장씩 분배하는 간격입니다.")]
    [Min(0f)]
    public float initialDealCardDelay = 0.09f;

    [Tooltip("교환 카드를 버린 뒤 새 카드를 받기 전 간격입니다.")]
    [Min(0f)]
    public float exchangeDiscardDelay = 0.16f;

    [Tooltip("교환용 새 카드를 한 장씩 받는 간격입니다.")]
    [Min(0f)]
    public float exchangeDealCardDelay = 0.12f;

    [Header("Audio")]
    [Tooltip("행동 음성, 카드, 칩, 승리 효과음을 관리하는 오디오 매니저입니다.")]
    public PokerAudioManager audioManager;

    [Header("Betting Chip Animation")]
    [Tooltip("플레이어가 지불한 금액을 중앙 테이블로 던지는 UI 칩 연출 관리자입니다.")]
    public PokerChipBetAnimator chipBetAnimator;

    [Tooltip("게임 시작 시 SB/BB 칩도 테이블로 던지는 연출을 실행합니다.")]
    public bool animateBlindChips = true;

    [Header("Winner Chip Collection")]
    [Tooltip("게임 종료 후 테이블 위 칩을 승자 플레이어 위치로 모읍니다.")]
    public bool collectTableChipsToWinners = true;

    [Tooltip("위너 표시와 승리 사운드가 나온 뒤 칩 회수를 시작하기 전 대기 시간입니다.")]
    [Min(0f)]
    public float winnerDisplayBeforeChipCollectDelay = 1f;

    [Header("Betting Action Icon Sprites")]
    [Tooltip("가장 최근에 행동한 플레이어에게 표시할 강조 아이콘 9종입니다.")]
    public BettingActionSpriteSet currentTurnActionSprites =
        new BettingActionSpriteSet();

    [Tooltip("그보다 앞서 행동한 플레이어들에게 표시할 지난 행동 아이콘 9종입니다.")]
    public BettingActionSpriteSet previousTurnActionSprites =
        new BettingActionSpriteSet();

    [Header("Game Start")]
    [Tooltip("앱 실행 시 홈 화면 뒤에 표시될 플레이어 데이터를 시작 금액으로 한 번 초기화합니다. 실제 세션 시작 버튼을 누를 때 다시 초기화됩니다.")]
    public bool initializePlayerMoneyOnAwake = true;

    [Tooltip("이전 버전 호환용 필드입니다. 이제 앱 실행 시에는 항상 홈 화면이 먼저 열리며 자동 게임 시작은 실행되지 않습니다.")]
    public bool autoStartGame = false;

    [Header("Home Screen And Player Seats")]
    [Tooltip("앱 최초 실행, 사용자 파산, 상대가 한 명 이하로 남았을 때 표시할 전체 화면 홈 오브젝트입니다.")]
    public GameObject homeObject;

    [Tooltip("Player Number 0~4 순서로 플레이어 마스크 오브젝트 5개를 등록합니다. 자리에서 나간 플레이어는 해당 마스크도 함께 꺼집니다.")]
    public GameObject[] playerMasks = new GameObject[5];

    [Tooltip("홈 화면에서 Player 1~4의 성향 토글과 슬라이더를 관리하는 컴포넌트입니다. 비워두면 Home Object 자식에서 자동으로 찾습니다.")]
    public HomeAISettingsController homeAISettingsController;

    [Tooltip("홈의 게임 시작 버튼을 누른 뒤 실제 첫 판을 시작하기 전 대기 시간입니다.")]
    [Min(0f)]
    public float homeStartGameDelay = 0.5f;

    [Tooltip("결과 화면을 보여준 뒤 보유 금액이 0인 플레이어가 자리에서 나가기 전 대기 시간입니다.")]
    [Min(0f)]
    public float bustedPlayerExitDelay = 0.8f;

    [SerializeField] private bool isHomeScreenOpen = true;
    [SerializeField] private bool isSessionActive;

    [Header("Next Game")]
    [Tooltip("쇼다운 종료 후 자동으로 다음 게임을 시작합니다.")]
    public bool autoStartNextGame = false;

    [Min(0f)]
    public float nextGameDelay = 3f;

    [Header("Advanced Computer AI")]
    [Tooltip("Player 1~4가 FiveCardDrawAI와 PokerAIBrain을 사용해 실제 판단하도록 합니다.")]
    public bool useAdvancedComputerAI = true;

    [Tooltip("컴퓨터 PlayerControl에 FiveCardDrawAI가 없으면 실행 중 자동으로 추가합니다.")]
    public bool autoAddAIComponents = true;

    [Tooltip("모든 플레이어의 공개 행동, 교환 장수와 전판 결과를 누적하는 기록 관리자입니다. 비워두면 같은 오브젝트에서 찾거나 자동 생성합니다.")]
    public PokerAIHistoryTracker aiHistoryTracker;

    [Tooltip("각 AI의 판단 이유를 Console에 출력합니다. 개별 FiveCardDrawAI의 Log Decision Details와 함께 사용할 수 있습니다.")]
    public bool logAIDecisions = true;

    [Header("Temporary Computer AI Fallback")]
    [Tooltip("고급 AI 컴포넌트가 없거나 비활성화된 경우에만 단순 콜/체크 AI를 대신 사용합니다.")]
    public bool useTemporaryComputerAI = true;

    [Min(0f)]
    public float computerActionDelay = 0.7f;

    [Header("Legacy Text UI")]
    public Text phaseText;
    public Text potText;
    public Text currentCallText;

    [Header("Error Alarm UI")]
    [Tooltip("ShowErrorAlarm(string) 호출 시 껐다가 다시 켜서 애니메이션을 재생할 알람 오브젝트입니다.")]
    public GameObject errorAlarmObject;

    [Tooltip("알람 문구를 표시할 Text입니다. 비워두면 Error Alarm Object 자식에서 자동으로 찾습니다.")]
    public Text errorAlarmText;

    [Tooltip("사용자 보유 칩이 0원이 되어 홈 화면으로 이동할 때 표시할 문구입니다.")]
    public string humanBustedHomeMessage =
        "올인되셨습니다. 게임을 다시 시작하세요.";

    [Header("Pause UI")]
    [Tooltip("게임 일시정지 시 표시할 전체 화면 오브젝트입니다. 게임으로 돌아가기와 홈으로 돌아가기 버튼을 이 오브젝트 아래에 배치합니다.")]
    public GameObject pauseObject;

    [Tooltip("일시정지 중 현재 재생 중인 게임 오디오도 함께 멈춥니다.")]
    public bool pauseAudioWithGame = true;

    [SerializeField] private bool isPaused;

    [Header("Table Card Count UI")]
    [Tooltip("아직 플레이어에게 분배되지 않은 덱의 남은 카드 수만 숫자로 표시합니다.")]
    public Text remainingCardText;

    [Tooltip("이번 판에서 교환으로 버린 카드의 누적 장수만 숫자로 표시합니다.")]
    public Text exchangedCardText;

    [Header("Round State Objects")]
    [Tooltip("첫 번째 베팅 라운드에서만 켜지는 상태 오브젝트입니다.")]
    public GameObject firstBettingRoundObject;

    [Tooltip("카드 교환 라운드에서만 켜지는 상태 오브젝트입니다.")]
    public GameObject exchangeRoundObject;

    [Tooltip("마지막 베팅 라운드에서만 켜지는 상태 오브젝트입니다.")]
    public GameObject finalBettingRoundObject;

    [Header("Human Action Boxes")]
    [Tooltip("기본적으로 표시하며 교환 페이즈에서만 숨길 베팅 박스입니다.")]
    public GameObject bettingBox;

    [Tooltip("교환 페이즈에서만 표시할 교환 박스입니다.")]
    public GameObject exchangeBox;

    [Header("Human Betting Toggle UI")]
    [Tooltip("Player 0의 다이 토글입니다.")]
    public Toggle foldToggle;

    [Tooltip("Player 0이 콜할 금액이 없을 때 두 번째 칸에 표시할 삥 토글입니다.")]
    public Toggle pingToggle;

    [Tooltip("Player 0이 콜할 금액이 있을 때 두 번째 칸에 표시할 따당 토글입니다.")]
    public Toggle doubleToggle;

    [Tooltip("맞춰야 할 금액이 있을 때 세 번째 칸에 표시할 콜 토글입니다.")]
    public Toggle callToggle;

    [Tooltip("맞춰야 할 금액이 없을 때 세 번째 칸에 표시할 체크 토글입니다.")]
    public Toggle checkToggle;

    [Tooltip("콜 이후 예상 팟의 1/4 금액을 레이즈하는 쿼터 토글입니다.")]
    public Toggle quarterToggle;

    [Tooltip("콜 이후 예상 팟의 1/2 금액을 레이즈하는 하프 토글입니다.")]
    public Toggle halfToggle;

    [Tooltip("카드 교환 후 마지막 베팅에서, 보유 금액 전체가 이번 판 맥스 한도 이내일 때 여섯 번째 칸에 표시할 올인 토글입니다. 첫 번째 베팅에서는 비활성화됩니다.")]
    public Toggle allInToggle;

    [Tooltip("카드 교환 후 마지막 베팅에서, 보유 금액 전체가 이번 판 맥스 한도를 초과할 때 여섯 번째 칸에 표시할 맥스 토글입니다. 첫 번째 베팅에서는 비활성화됩니다.")]
    public Toggle maxToggle;

    [Header("Human Exchange Toggle UI")]
    [Tooltip("카드를 교환하지 않고 넘기는 패스 토글입니다.")]
    public Toggle passToggle;

    [Tooltip("현재 선택한 카드를 교환하는 토글입니다. 선택 카드가 없으면 비활성화됩니다.")]
    public Toggle exchangeToggle;

    [Header("Human Betting Amount Box UI")]
    [Tooltip("다이는 금액이 없으므로 박스를 항상 끕니다.")]
    public BettingAmountUIReference foldAmountUI =
        new BettingAmountUIReference();

    public BettingAmountUIReference pingAmountUI =
        new BettingAmountUIReference();

    public BettingAmountUIReference doubleAmountUI =
        new BettingAmountUIReference();

    public BettingAmountUIReference callAmountUI =
        new BettingAmountUIReference();

    [Tooltip("체크는 금액이 없으므로 박스를 항상 끕니다.")]
    public BettingAmountUIReference checkAmountUI =
        new BettingAmountUIReference();

    public BettingAmountUIReference quarterAmountUI =
        new BettingAmountUIReference();

    public BettingAmountUIReference halfAmountUI =
        new BettingAmountUIReference();

    public BettingAmountUIReference allInAmountUI =
        new BettingAmountUIReference();

    public BettingAmountUIReference maxAmountUI =
        new BettingAmountUIReference();

    [Header("Human Betting Reservation")]
    [Tooltip("상대 턴에 예약한 베팅이 내 턴이 된 뒤 자동 실행되기까지 기다리는 시간입니다. 내 턴에 직접 누른 행동은 즉시 실행됩니다.")]
    [Min(0f)]
    public float reservedHumanBettingExecutionDelay = 0.45f;

    [Tooltip("Player 0이 미리 선택한 예약 행동입니다. -1이면 예약이 없습니다.")]
    [SerializeField] private int reservedHumanBettingAction = -1;

    private bool isExecutingReservedHumanBettingAction;
    private Coroutine reservedHumanBettingDelayCoroutine;

    [Header("Human Exchange Reservation Runtime")]
    [Tooltip("Player 0이 미리 선택한 교환 행동입니다. -1이면 예약이 없습니다.")]
    [SerializeField] private int reservedHumanExchangeAction = -1;

    private bool isExecutingReservedHumanExchangeAction;

    [Header("Runtime State")]
    [SerializeField] private GamePhase currentPhase = GamePhase.Preparing;
    [SerializeField] private int gameNumber;
    [SerializeField] private int dealerIndex = -1;
    [SerializeField] private int smallBlindIndex = -1;
    [SerializeField] private int bigBlindIndex = -1;
    [SerializeField] private int currentTurnIndex = -1;
    [SerializeField] private long totalPot;

    [Tooltip("정산이 끝난 뒤에도 다음 판이 시작될 때까지 팟 UI에 유지할 직전 판의 총 팟입니다.")]
    [SerializeField] private long completedHandPot;

    [SerializeField] private long currentHighestBet;
    [SerializeField] private long lastRaiseIncrement;
    [SerializeField] private bool isGameFinished;
    [SerializeField] private bool isDealingCards;
    [SerializeField] private bool isExchangeAnimating;
    [SerializeField] private bool isPhaseTransitioning;
    [SerializeField] private bool isWaitingForWinnerDisplay;

    [Tooltip("현재 실제 카드 교환 연출을 진행 중인 플레이어 번호입니다. 없으면 -1입니다.")]
    [SerializeField] private int exchangingPlayerNumber = -1;

    private readonly List<int> deck = new List<int>(CardUtility.TotalCardCount);
    private readonly List<int> discardedCards = new List<int>();

    // 현재 베팅 페이즈에서 각 플레이어가 마지막으로 선택한 행동을 기억합니다.
    // 가장 최근 행동자 한 명은 currentTurnActionSprites를 사용하고,
    // 그 이전 행동자들은 previousTurnActionSprites를 사용합니다.
    private readonly Dictionary<PlayerControl, BettingAction>
        displayedBettingActions =
            new Dictionary<PlayerControl, BettingAction>();

    private PlayerControl latestActionIconPlayer;

    private int deckCursor;
    private Coroutine nextGameCoroutine;
    private Coroutine gameSequenceCoroutine;
    private Coroutine exchangeCoroutine;
    private Coroutine phaseTransitionCoroutine;
    private Coroutine showdownResultCoroutine;
    private Coroutine winnerPresentationCoroutine;
    private Coroutine handCompletionCoroutine;
    private Coroutine homeStartCoroutine;

    private float timeScaleBeforePause = 1f;
    private bool audioPauseBeforePause;

    public GamePhase CurrentPhase
    {
        get { return currentPhase; }
    }

    public int PhaseNumber
    {
        get { return (int)currentPhase; }
    }

    public int MaxExchangeCards
    {
        get { return maxExchangeCards; }
    }

    public bool IsPaused
    {
        get { return isPaused; }
    }

    public long TotalPot
    {
        get { return totalPot; }
    }

    public long CurrentHighestBet
    {
        get { return currentHighestBet; }
    }

    public int DealerIndex
    {
        get { return dealerIndex; }
    }

    public int SmallBlindIndex
    {
        get { return smallBlindIndex; }
    }

    public int BigBlindIndex
    {
        get { return bigBlindIndex; }
    }

    public PlayerControl CurrentTurnPlayer
    {
        get
        {
            if (isHomeScreenOpen ||
                currentTurnIndex < 0 ||
                currentTurnIndex >= players.Count)
            {
                return null;
            }

            PlayerControl player = players[currentTurnIndex];

            return IsPlayerSeated(player)
                ? player
                : null;
        }
    }

    public bool IsHomeScreenOpen
    {
        get { return isHomeScreenOpen; }
    }

    public bool IsSessionActive
    {
        get { return isSessionActive; }
    }

    public bool IsDealingCards
    {
        get { return isDealingCards; }
    }

    public bool IsExchangeAnimating
    {
        get { return isExchangeAnimating; }
    }

    public int GameNumber
    {
        get { return gameNumber; }
    }

    public int CurrentTurnIndex
    {
        get { return currentTurnIndex; }
    }

    public bool IsGameFinished
    {
        get { return isGameFinished; }
    }

    public bool IsPhaseTransitioning
    {
        get { return isPhaseTransitioning; }
    }

    public PokerAIHistoryTracker AIHistoryTracker
    {
        get { return aiHistoryTracker; }
    }

    public int ExchangingPlayerNumber
    {
        get { return exchangingPlayerNumber; }
    }

    /// <summary>
    /// 지정한 플레이어가 현재 실제 카드 교환 연출을 진행 중인지 반환합니다.
    /// 다른 플레이어가 교환 중인 상태와 구분하기 위해 사용합니다.
    /// </summary>
    public bool IsPlayerCurrentlyExchanging(
        int playerNumber)
    {
        return isExchangeAnimating &&
               exchangingPlayerNumber == playerNumber;
    }

    /// <summary>
    /// 카드 번호에 맞는 앞면 스프라이트를 반환합니다.
    /// </summary>
    public Sprite GetCardFaceSprite(
        int cardNumber)
    {
        if (!CardUtility.IsValidCardNumber(cardNumber) ||
            cardSprites == null ||
            cardNumber >= cardSprites.Length)
        {
            return null;
        }

        return cardSprites[cardNumber];
    }

    /// <summary>
    /// 카드가 플레이어 손패에 있다면 이동할 슬롯을 반환합니다.
    /// </summary>
    public bool TryGetCardTarget(
        int cardNumber,
        out RectTransform target,
        out int ownerPlayerNumber,
        out int handIndex)
    {
        target = null;
        ownerPlayerNumber = -1;
        handIndex = -1;

        for (int playerIndex = 0;
             playerIndex < players.Count;
             playerIndex++)
        {
            PlayerControl player = players[playerIndex];

            if (!IsPlayerSeated(player))
            {
                continue;
            }

            int foundHandIndex =
                player.FindHandIndex(cardNumber);

            if (foundHandIndex < 0)
            {
                continue;
            }

            target =
                player.GetCardPosition(foundHandIndex);

            ownerPlayerNumber =
                player.playerNumber;

            handIndex = foundHandIndex;

            return target != null;
        }

        return false;
    }

    /// <summary>
    /// 실제 카드 오브젝트를 눌렀을 때 사용자 손패의 선택 상태를 토글합니다.
    /// Player 0이 카드 5장을 받은 뒤부터 쇼다운 전까지 선택할 수 있습니다.
    /// 상대 플레이어의 교환 연출 중에도 사용자 선택은 유지하고 변경할 수 있습니다.
    /// </summary>
    public void ToggleExchangeSelectionByCardNumber(
        int cardNumber)
    {
        if (isDealingCards ||
            currentPhase == GamePhase.Showdown ||
            !CardUtility.IsValidCardNumber(cardNumber))
        {
            return;
        }

        RectTransform target;
        int ownerPlayerNumber;
        int handIndex;

        if (!TryGetCardTarget(
                cardNumber,
                out target,
                out ownerPlayerNumber,
                out handIndex))
        {
            return;
        }

        if (ownerPlayerNumber != 0)
        {
            return;
        }

        PlayerControl ownerPlayer =
            GetPlayerByNumber(ownerPlayerNumber);

        if (ownerPlayer == null ||
            ownerPlayer.IsFolded ||
            ownerPlayer.HasExchangedThisGame ||
            !ownerPlayer.HasValidFiveCardHand() ||
            IsPlayerCurrentlyExchanging(ownerPlayerNumber))
        {
            return;
        }

        ownerPlayer.ToggleExchangeCard(handIndex);
    }

    /// <summary>
    /// 지정 카드가 현재 사용자 선택 카드인지 반환합니다.
    /// Player 0 본인이 교환 중일 때만 선택 상승을 잠시 해제합니다.
    /// 상대 플레이어가 교환 중일 때는 기존 선택 상태를 그대로 유지합니다.
    /// </summary>
    public bool IsCardSelectedForExchange(
        int cardNumber)
    {
        if (isDealingCards ||
            currentPhase == GamePhase.Showdown ||
            !CardUtility.IsValidCardNumber(cardNumber))
        {
            return false;
        }

        RectTransform target;
        int ownerPlayerNumber;
        int handIndex;

        if (!TryGetCardTarget(
                cardNumber,
                out target,
                out ownerPlayerNumber,
                out handIndex))
        {
            return false;
        }

        if (ownerPlayerNumber != 0)
        {
            return false;
        }

        PlayerControl ownerPlayer =
            GetPlayerByNumber(ownerPlayerNumber);

        if (ownerPlayer == null ||
            ownerPlayer.IsFolded ||
            ownerPlayer.HasExchangedThisGame ||
            !ownerPlayer.HasValidFiveCardHand() ||
            IsPlayerCurrentlyExchanging(ownerPlayerNumber))
        {
            return false;
        }

        return ownerPlayer.IsExchangeCardSelected(
            handIndex
        );
    }

    /// <summary>
    /// Player Number로 등록된 플레이어를 찾습니다.
    /// </summary>
    public PlayerControl GetPlayerByNumber(
        int playerNumber)
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player != null &&
                player.playerNumber == playerNumber)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>
    /// 교환으로 버린 카드인지 확인합니다.
    /// </summary>
    public bool IsCardDiscarded(
        int cardNumber)
    {
        return discardedCards.Contains(cardNumber);
    }

    /// <summary>
    /// 모든 카드의 상태를 사용 전 상태로 되돌리고,
    /// 분배 전 카드 더미 위치로 즉시 이동시킵니다.
    /// 새 게임 시작 시 이동 애니메이션 없이 한 번에 회수됩니다.
    /// </summary>
    public void ResetAllCardsImmediately()
    {
        CardControl[] cardControls =
            FindObjectsOfType<CardControl>(true);

        for (int i = 0; i < cardControls.Length; i++)
        {
            if (cardControls[i] == null)
            {
                continue;
            }

            cardControls[i].ResetCardStateImmediate();
        }
    }

    private void Awake()
    {
        // 이전 플레이 모드나 씬 전환에서 일시정지 값이 남아 있어도
        // 이 게임 씬은 항상 정상 시간으로 시작합니다.
        Time.timeScale = 1f;
        AudioListener.pause = false;
        isPaused = false;
        SetPauseScreenVisible(false);

        PreparePlayerList();
        ResolveHomeAISettingsController();

        if (initializePlayerMoneyOnAwake)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                {
                    players[i].InitializeForSession();
                }
            }
        }

        isSessionActive = false;
        SetHomeScreenVisible(true);
        SetCurrentTurn(-1);
        RefreshGameUI();
    }

    private void Start()
    {
        // 최초 실행은 항상 홈 화면에서 시작합니다.
        // autoStartGame 값이 이전 씬 데이터에 true로 남아 있어도 자동 시작하지 않습니다.
        SetHomeScreenVisible(true);
    }

    private void OnDisable()
    {
        RestoreFromPause();

        CancelAllComputerAITurns();
        StopAllCoroutines();

        nextGameCoroutine = null;
        gameSequenceCoroutine = null;
        exchangeCoroutine = null;
        phaseTransitionCoroutine = null;
        showdownResultCoroutine = null;
        winnerPresentationCoroutine = null;
        handCompletionCoroutine = null;
        homeStartCoroutine = null;

        isDealingCards = false;
        isExchangeAnimating = false;
        isPhaseTransitioning = false;
        isWaitingForWinnerDisplay = false;
        exchangingPlayerNumber = -1;

        reservedHumanBettingAction = -1;
        isExecutingReservedHumanBettingAction = false;
        reservedHumanBettingDelayCoroutine = null;

        reservedHumanExchangeAction = -1;
        isExecutingReservedHumanExchangeAction = false;

        ClearAllBettingActionIcons();
    }

    private void CancelAllComputerAITurns()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player == null)
            {
                continue;
            }

            FiveCardDrawAI ai =
                player.GetComponent<FiveCardDrawAI>();

            if (ai != null)
            {
                ai.CancelPendingTurn();
            }
        }
    }

    private void PreparePlayerList()
    {
        players.RemoveAll(delegate (PlayerControl player)
        {
            return player == null;
        });

        if (players.Count == 0)
        {
            PlayerControl[] foundPlayers = FindObjectsOfType<PlayerControl>(true);
            players.AddRange(foundPlayers);
        }

        players.Sort(delegate (PlayerControl a, PlayerControl b)
        {
            return a.playerNumber.CompareTo(b.playerNumber);
        });

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];
            player.gameManager = this;

            if (useAdvancedComputerAI &&
                autoAddAIComponents &&
                player.IsComputerPlayer &&
                player.GetComponent<FiveCardDrawAI>() == null)
            {
                player.gameObject.AddComponent<FiveCardDrawAI>();
            }
        }

        PrepareAIHistoryTracker();
        ValidatePlayerNumbers();

        if (players.Count != 5)
        {
            Debug.LogWarning(
                "현재 게임은 플레이어 5명을 기준으로 합니다. 등록된 플레이어 수: " +
                players.Count
            );
        }
    }

    /// <summary>
    /// Player 1=보수형, Player 2=공격형, Player 3=계산형,
    /// Player 4=변칙형 권장 구성을 한 번에 적용합니다.
    /// </summary>
    [ContextMenu("Apply Recommended Four AI Styles")]
    public void ApplyRecommendedFourAIStyles()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player == null || !player.IsComputerPlayer)
            {
                continue;
            }

            PokerAIStyle style;

            switch (player.playerNumber)
            {
                case 1:
                    style = PokerAIStyle.Conservative;
                    break;
                case 2:
                    style = PokerAIStyle.Aggressive;
                    break;
                case 3:
                    style = PokerAIStyle.Calculated;
                    break;
                case 4:
                    style = PokerAIStyle.Trickster;
                    break;
                default:
                    style = PokerAIStyle.Calculated;
                    break;
            }

            player.SetAIStyle(style, true);
        }
    }

    private void PrepareAIHistoryTracker()
    {
        if (aiHistoryTracker == null)
        {
            aiHistoryTracker = GetComponent<PokerAIHistoryTracker>();
        }

        if (aiHistoryTracker == null && useAdvancedComputerAI)
        {
            aiHistoryTracker =
                gameObject.AddComponent<PokerAIHistoryTracker>();
        }

        if (aiHistoryTracker != null)
        {
            aiHistoryTracker.Initialize(players);
        }
    }

    private void ValidatePlayerNumbers()
    {
        HashSet<int> usedNumbers = new HashSet<int>();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!usedNumbers.Add(player.playerNumber))
            {
                Debug.LogError(
                    "중복된 Player Number가 있습니다: " +
                    player.playerNumber
                );
            }
        }
    }

    #region Pause

    /// <summary>
    /// 퍼즈 버튼 OnClick에 연결합니다.
    /// 현재 게임 진행과 일반 게임 애니메이션/코루틴을 Time.timeScale로 정지합니다.
    /// </summary>
    public void PauseGame()
    {
        if (isPaused ||
            isHomeScreenOpen ||
            !isSessionActive)
        {
            return;
        }

        timeScaleBeforePause = Time.timeScale;

        if (timeScaleBeforePause <= 0f)
        {
            timeScaleBeforePause = 1f;
        }

        audioPauseBeforePause = AudioListener.pause;
        isPaused = true;

        SetPauseScreenVisible(true);

        if (pauseAudioWithGame)
        {
            AudioListener.pause = true;
        }

        Time.timeScale = 0f;
    }

    /// <summary>
    /// 퍼즈 화면의 게임으로 돌아가기 버튼 OnClick에 연결합니다.
    /// 중지 전의 시간 배율과 오디오 상태를 복원합니다.
    /// </summary>
    public void ResumeGame()
    {
        RestoreFromPause();
    }

    /// <summary>
    /// 퍼즈 화면의 홈으로 돌아가기 버튼 OnClick에 연결합니다.
    /// 현재 판을 완전히 포기하고 홈으로 이동합니다.
    /// 이후 홈에서 게임을 시작하면 StartGameFromHome이 모든 플레이어,
    /// 금액, 카드, 딜러 위치와 게임 번호를 처음부터 초기화합니다.
    /// </summary>
    public void ReturnHomeFromPause()
    {
        RestoreFromPause();
        ShowHomeScreen();
        ResetAllCardsImmediately();
    }

    private void RestoreFromPause()
    {
        if (isPaused)
        {
            Time.timeScale =
                timeScaleBeforePause > 0f
                    ? timeScaleBeforePause
                    : 1f;

            if (pauseAudioWithGame)
            {
                AudioListener.pause = audioPauseBeforePause;
            }
        }
        else if (Time.timeScale <= 0f)
        {
            // 퍼즈 상태 플래그가 유실되어도 홈/새 게임이 멈춘 상태로 남지 않게 합니다.
            Time.timeScale = 1f;
        }

        isPaused = false;
        SetPauseScreenVisible(false);
    }

    private void SetPauseScreenVisible(bool visible)
    {
        if (pauseObject != null &&
            pauseObject.activeSelf != visible)
        {
            pauseObject.SetActive(visible);
        }
    }

    #endregion

    #region Home Screen And Session

    /// <summary>
    /// 홈 화면의 게임 시작 버튼 OnClick에 연결합니다.
    /// 모든 플레이어와 마스크를 다시 켜고 시작 금액으로 초기화한 뒤,
    /// 설정된 지연 시간이 지나면 첫 게임을 시작합니다.
    /// </summary>
    public void StartGameButton()
    {
        StartGameFromHome();
    }

    public void StartGameFromHome()
    {
        RestoreFromPause();

        if (homeStartCoroutine != null)
        {
            return;
        }

        StopCurrentGameFlow();

        isSessionActive = true;
        isGameFinished = false;
        isWaitingForWinnerDisplay = false;
        currentPhase = GamePhase.Preparing;
        currentTurnIndex = -1;

        gameNumber = 0;
        dealerIndex = -1;
        smallBlindIndex = -1;
        bigBlindIndex = -1;

        totalPot = 0L;
        completedHandPot = 0L;
        currentHighestBet = 0L;
        lastRaiseIncrement = GetBaseMinimumBet();

        deck.Clear();
        discardedCards.Clear();
        deckCursor = 0;

        if (chipBetAnimator != null)
        {
            chipBetAnimator.ClearTableChips();
        }

        ResetAllPlayersForNewSession();
        ApplyHomeAISettingsToPlayers();
        ResetAllCardsImmediately();
        ClearAllBettingActionIcons();
        ClearHumanBettingReservation(true);
        ClearHumanExchangeReservation(true);

        SetHomeScreenVisible(false);
        RefreshGameUI();

        homeStartCoroutine =
            StartCoroutine(StartGameFromHomeRoutine());
    }

    private IEnumerator StartGameFromHomeRoutine()
    {
        if (homeStartGameDelay > 0f)
        {
            yield return new WaitForSeconds(homeStartGameDelay);
        }
        else
        {
            yield return null;
        }

        homeStartCoroutine = null;

        if (!isSessionActive || isHomeScreenOpen)
        {
            yield break;
        }

        StartGame();
    }

    /// <summary>
    /// 외부 홈 버튼 또는 사용자 파산 처리에서도 사용할 수 있습니다.
    /// 진행 중인 코루틴, AI 턴, 예약 행동과 자동 다음 게임을 모두 정지합니다.
    /// </summary>
    public void ShowHomeScreen()
    {
        RestoreFromPause();
        StopCurrentGameFlow();

        isSessionActive = false;
        isGameFinished = true;
        isWaitingForWinnerDisplay = false;
        currentPhase = GamePhase.Preparing;
        currentTurnIndex = -1;

        totalPot = 0L;
        completedHandPot = 0L;
        currentHighestBet = 0L;

        ClearHumanBettingReservation(true);
        ClearHumanExchangeReservation(true);
        ClearAllBettingActionIcons();

        if (chipBetAnimator != null)
        {
            chipBetAnimator.ClearTableChips();
        }

        SetHomeScreenVisible(true);
        RefreshGameUI();
    }

    private void StopCurrentGameFlow()
    {
        CancelAllComputerAITurns();
        StopAllCoroutines();

        nextGameCoroutine = null;
        gameSequenceCoroutine = null;
        exchangeCoroutine = null;
        phaseTransitionCoroutine = null;
        showdownResultCoroutine = null;
        winnerPresentationCoroutine = null;
        handCompletionCoroutine = null;
        homeStartCoroutine = null;

        isDealingCards = false;
        isExchangeAnimating = false;
        isPhaseTransitioning = false;
        isWaitingForWinnerDisplay = false;
        exchangingPlayerNumber = -1;

        ClearHumanBettingReservation(true);
        isExecutingReservedHumanBettingAction = false;
        reservedHumanBettingDelayCoroutine = null;

        ClearHumanExchangeReservation(true);
        isExecutingReservedHumanExchangeAction = false;

        SetCurrentTurn(-1);
    }

    private void ResetAllPlayersForNewSession()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player == null)
            {
                continue;
            }

            if (!player.gameObject.activeSelf)
            {
                player.gameObject.SetActive(true);
            }

            player.gameManager = this;
            SetPlayerMaskActive(player.playerNumber, true);
            player.InitializeForSession();
        }

        if (aiHistoryTracker != null)
        {
            aiHistoryTracker.Initialize(players);
        }
    }

    private void ResolveHomeAISettingsController()
    {
        if (homeAISettingsController != null)
        {
            homeAISettingsController.InitializeIfNeeded();
            return;
        }

        if (homeObject != null)
        {
            homeAISettingsController =
                homeObject.GetComponentInChildren<HomeAISettingsController>(
                    true
                );
        }

        if (homeAISettingsController == null)
        {
            homeAISettingsController =
                FindObjectOfType<HomeAISettingsController>(true);
        }

        if (homeAISettingsController != null)
        {
            homeAISettingsController.InitializeIfNeeded();
        }
    }

    private void ApplyHomeAISettingsToPlayers()
    {
        ResolveHomeAISettingsController();

        if (homeAISettingsController == null)
        {
            Debug.LogWarning(
                "Home AI Settings Controller가 없어 PlayerControl의 현재 AI 설정을 그대로 사용합니다."
            );
            return;
        }

        homeAISettingsController.ApplySettingsToPlayers(players);
    }

    private void SetHomeScreenVisible(bool visible)
    {
        isHomeScreenOpen = visible;

        if (homeObject != null &&
            homeObject.activeSelf != visible)
        {
            homeObject.SetActive(visible);
        }
    }

    #endregion

    #region Game Start And Next Game

    /// <summary>
    /// 새로운 한 판을 시작합니다.
    /// 첫 호출에서는 Player 0이 딜러가 되고,
    /// 이후 호출할 때마다 딜러가 한 자리씩 이동합니다.
    /// </summary>
    public void StartGame()
    {
        if (!isSessionActive || isHomeScreenOpen)
        {
            Debug.LogWarning(
                "홈 화면이 열려 있거나 게임 세션이 시작되지 않아 게임을 시작하지 않습니다."
            );
            return;
        }

        // 수동 다음 게임 버튼이 퇴장 지연보다 먼저 눌려도
        // 0원 플레이어가 다음 판에 다시 참가하지 않도록 시작 직전에 한 번 더 정리합니다.
        if (!ProcessBustedPlayersAfterHand())
        {
            return;
        }

        if (GetSeatedPlayerCount() < 2)
        {
            Debug.LogWarning(
                "자리에 남은 플레이어가 2명 미만이므로 홈 화면으로 이동합니다."
            );
            ShowHomeScreen();
            return;
        }

        CancelAllComputerAITurns();
        StopAllCoroutines();

        nextGameCoroutine = null;
        gameSequenceCoroutine = null;
        exchangeCoroutine = null;
        phaseTransitionCoroutine = null;
        showdownResultCoroutine = null;
        winnerPresentationCoroutine = null;
        handCompletionCoroutine = null;
        homeStartCoroutine = null;

        isDealingCards = false;
        isExchangeAnimating = false;
        isPhaseTransitioning = false;
        isWaitingForWinnerDisplay = false;
        exchangingPlayerNumber = -1;

        ClearHumanBettingReservation(true);
        isExecutingReservedHumanBettingAction = false;

        ClearHumanExchangeReservation(true);
        isExecutingReservedHumanExchangeAction = false;

        ClearAllBettingActionIcons();

        // 새 판이 시작되면 전판에 테이블 위에 쌓였던 모든 칩을 풀로 되돌립니다.
        if (chipBetAnimator != null)
        {
            chipBetAnimator.ClearTableChips();
        }

        gameNumber++;
        isGameFinished = false;

        currentPhase = GamePhase.Preparing;
        currentTurnIndex = -1;
        totalPot = 0L;
        completedHandPot = 0L;
        currentHighestBet = 0L;
        lastRaiseIncrement = GetBaseMinimumBet();

        deck.Clear();
        discardedCards.Clear();
        deckCursor = 0;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player))
            {
                continue;
            }

            // 보유 금액은 유지하고 새 판 데이터만 초기화합니다.
            // 0원이 된 플레이어는 이전 판 종료 처리에서 이미 자리에서 나갑니다.
            player.ResetForNewGame();
        }

        if (aiHistoryTracker != null)
        {
            aiHistoryTracker.BeginHand(
                players,
                gameNumber
            );
        }

        // 카드 리스트를 비운 직후 모든 실제 카드 오브젝트를
        // 이동 연출 없이 분배 전 카드 더미로 즉시 회수합니다.
        // 이때 앞면이 남지 않도록 기본 카드 뒷면도 강제로 적용합니다.
        ResetAllCardsImmediately();

        RotateDealerAndBlinds();
        CreateAndShuffleDeck();
        RefreshGameUI();

        gameSequenceCoroutine =
            StartCoroutine(StartGameSequence());
    }

    private IEnumerator StartGameSequence()
    {
        isDealingCards = true;

        // 새 게임 화면이 정리된 뒤 잠시 보여주고 블라인드 수거를 시작합니다.
        if (gameStartBeforeBlindDelay > 0f)
        {
            yield return new WaitForSeconds(
                gameStartBeforeBlindDelay
            );
        }

        yield return StartCoroutine(
            PostBlindsRoutine()
        );

        // 블라인드 칩이 테이블에 놓인 모습을 확인할 시간을 둔 뒤 카드를 분배합니다.
        if (blindsToInitialDealDelay > 0f)
        {
            yield return new WaitForSeconds(
                blindsToInitialDealDelay
            );
        }

        if (audioManager != null)
        {
            audioManager.PlayGameStartDealing();
        }

        yield return StartCoroutine(
            DealInitialCardsRoutine()
        );

        // 마지막 카드가 도착한 뒤 곧바로 버튼이 켜지지 않도록 잠시 대기합니다.
        if (initialDealToFirstBettingDelay > 0f)
        {
            yield return new WaitForSeconds(
                initialDealToFirstBettingDelay
            );
        }

        isDealingCards = false;
        gameSequenceCoroutine = null;

        // 최초 5장 분배와 정렬이 모두 끝난 직후 사용자에게
        // 추천 교환 카드를 한 번 자동으로 선택해 둡니다.
        ApplyAutomaticExchangeRecommendationToHumanPlayer();

        StartFirstBettingPhase();
    }

    /// <summary>
    /// 다음 게임 버튼 OnClick에 연결합니다.
    /// </summary>
    public void StartNextGameButton()
    {
        if (isHomeScreenOpen ||
            !isSessionActive ||
            winnerPresentationCoroutine != null ||
            handCompletionCoroutine != null)
        {
            return;
        }

        if (currentPhase != GamePhase.Showdown || !isGameFinished)
        {
            Debug.LogWarning("현재 게임이 아직 종료되지 않았습니다.");
            return;
        }

        StartGame();
    }

    private void ScheduleNextGame()
    {
        if (isHomeScreenOpen ||
            !isSessionActive ||
            !autoStartNextGame)
        {
            return;
        }

        if (nextGameCoroutine != null)
        {
            StopCoroutine(nextGameCoroutine);
        }

        nextGameCoroutine = StartCoroutine(StartNextGameAfterDelay());
    }

    private IEnumerator StartNextGameAfterDelay()
    {
        yield return new WaitForSeconds(nextGameDelay);

        nextGameCoroutine = null;

        if (!isHomeScreenOpen &&
            isSessionActive &&
            currentPhase == GamePhase.Showdown &&
            isGameFinished)
        {
            StartGame();
        }
    }

    private void RotateDealerAndBlinds()
    {
        int seatedCount = GetSeatedPlayerCount();

        dealerIndex = FindNextSeatedPlayerIndex(dealerIndex);

        if (dealerIndex < 0)
        {
            smallBlindIndex = -1;
            bigBlindIndex = -1;
            return;
        }

        if (seatedCount == 2)
        {
            // 헤즈업에서는 딜러가 SB, 상대가 BB입니다.
            smallBlindIndex = dealerIndex;
            bigBlindIndex = FindNextSeatedPlayerIndex(dealerIndex);
        }
        else
        {
            smallBlindIndex = FindNextSeatedPlayerIndex(dealerIndex);
            bigBlindIndex = FindNextSeatedPlayerIndex(smallBlindIndex);
        }

        for (int i = 0; i < players.Count; i++)
        {
            players[i].SetRoleIcons(
                i == dealerIndex,
                i == smallBlindIndex,
                i == bigBlindIndex
            );
        }

        Debug.Log(
            "게임 " + gameNumber +
            " / 딜러 Player " + players[dealerIndex].playerNumber +
            " / SB Player " + players[smallBlindIndex].playerNumber +
            " / BB Player " + players[bigBlindIndex].playerNumber
        );
    }

    private IEnumerator PostBlindsRoutine()
    {
        long sbPaid =
            players[smallBlindIndex].CommitBet(smallBlindAmount);

        totalPot += sbPaid;

        if (animateBlindChips)
        {
            PlayBetChipAnimation(
                players[smallBlindIndex],
                sbPaid
            );
        }

        currentHighestBet =
            players[smallBlindIndex].RoundBetMoney;

        lastRaiseIncrement = GetBaseMinimumBet();
        RefreshGameUI();

        if (blindPostInterval > 0f)
        {
            yield return new WaitForSeconds(
                blindPostInterval
            );
        }
        else
        {
            yield return null;
        }

        long bbPaid =
            players[bigBlindIndex].CommitBet(bigBlindAmount);

        totalPot += bbPaid;

        if (animateBlindChips)
        {
            PlayBetChipAnimation(
                players[bigBlindIndex],
                bbPaid
            );
        }

        currentHighestBet = Math.Max(
            players[smallBlindIndex].RoundBetMoney,
            players[bigBlindIndex].RoundBetMoney
        );

        lastRaiseIncrement = GetBaseMinimumBet();
        RefreshGameUI();
    }

    #endregion

    #region Deck And Deal

    private void CreateAndShuffleDeck()
    {
        deck.Clear();
        discardedCards.Clear();

        for (int cardNumber = 0;
             cardNumber < CardUtility.TotalCardCount;
             cardNumber++)
        {
            deck.Add(cardNumber);
        }

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);

            int temp = deck[i];
            deck[i] = deck[randomIndex];
            deck[randomIndex] = temp;
        }

        deckCursor = 0;
    }

    /// <summary>
    /// 딜러 왼쪽부터 실제 포커처럼 한 장씩, 총 5회에 걸쳐 분배합니다.
    /// 카드 번호가 플레이어 리스트에 추가되는 순간 CardControl이 해당 슬롯으로 이동합니다.
    /// </summary>
    private IEnumerator DealInitialCardsRoutine()
    {
        for (int cardRound = 0;
             cardRound < 5;
             cardRound++)
        {
            for (int seatOffset = 1;
                 seatOffset <= players.Count;
                 seatOffset++)
            {
                int targetIndex =
                    (dealerIndex + seatOffset) % players.Count;

                if (!IsPlayerSeated(players[targetIndex]))
                {
                    continue;
                }

                int cardNumber = DrawCard();

                if (cardNumber >= 0)
                {
                    players[targetIndex].ReceiveCard(cardNumber);

                    if (audioManager != null)
                    {
                        audioManager.PlayCardDealOne();
                    }
                }

                if (initialDealCardDelay > 0f)
                {
                    yield return new WaitForSeconds(
                        initialDealCardDelay
                    );
                }
                else
                {
                    yield return null;
                }
            }
        }

        // 5장 분배가 모두 끝난 뒤 각 플레이어의 손패를
        // 숫자 오름차순, 같은 숫자는 ♣, ♦, ♥, ♠ 순서로 정렬합니다.
        // 리스트 순서가 바뀌면 CardControl이 새 슬롯으로 자연스럽게 이동합니다.
        for (int i = 0; i < players.Count; i++)
        {
            if (IsPlayerSeated(players[i]))
            {
                players[i].SortCardsByRankThenSuit();
            }
        }

        LogPlayerCards();

        // 사람 플레이어는 최초 5장을 모두 받은 직후 족보를 표시합니다.
        RefreshHumanPlayerHandRank();
    }

    private void LogPlayerCards()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player))
            {
                continue;
            }

            string cardLog = string.Empty;

            for (int cardIndex = 0;
                 cardIndex < player.cardNumbers.Count;
                 cardIndex++)
            {
                int cardNumber =
                    player.cardNumbers[cardIndex];

                if (!CardUtility.IsValidCardNumber(cardNumber))
                {
                    continue;
                }

                cardLog +=
                    CardUtility.GetCardName(cardNumber) +
                    " ";
            }

            Debug.Log(
                "Player " + player.playerNumber +
                " 카드: " + cardLog
            );
        }
    }

    private int DrawCard()
    {
        if (deckCursor >= deck.Count)
        {
            Debug.LogError("덱에 남은 카드가 없습니다.");
            return -1;
        }

        int cardNumber = deck[deckCursor];
        deckCursor++;

        // 카드가 실제로 덱에서 빠질 때마다 남은 카드 수를 즉시 갱신합니다.
        RefreshTableCardCountUI();

        return cardNumber;
    }

    /// <summary>
    /// 최초 카드 5장 분배가 끝난 Player 0의 손패에 추천 교환 선택을 적용합니다.
    /// </summary>
    private void ApplyAutomaticExchangeRecommendationToHumanPlayer()
    {
        if (!autoSelectRecommendedExchangeCards)
        {
            return;
        }

        PlayerControl humanPlayer = GetPlayerByNumber(0);

        if (humanPlayer == null)
        {
            return;
        }

        ApplyAutomaticExchangeRecommendation(humanPlayer);
    }

    /// <summary>
    /// 최초 손패를 분석하여 추천할 교환 카드 인덱스를 자동 선택합니다.
    /// 추천은 최초 분배 직후 한 번만 적용되며, 교환 전까지 사용자가 직접 변경할 수 있습니다.
    /// </summary>
    private void ApplyAutomaticExchangeRecommendation(
        PlayerControl player)
    {
        if (!autoSelectRecommendedExchangeCards ||
            !IsPlayerSeated(player) ||
            !player.IsHumanPlayer ||
            player.IsFolded ||
            !player.HasValidFiveCardHand())
        {
            return;
        }

        List<int> recommendedIndexes =
            CreateRecommendedExchangeIndexes(
                player.cardNumbers
            );

        player.SetExchangeSelection(
            recommendedIndexes
        );

        Debug.Log(
            CreateExchangeRecommendationLog(
                player,
                recommendedIndexes
            )
        );
    }

    /// <summary>
    /// 파이브 카드 드로우 기본 전략에 따라 버릴 카드 인덱스를 계산합니다.
    /// 완성 스트레이트 이상과 포카드는 유지하고,
    /// 트리플은 2장, 투페어는 1장, 원페어는 3장을 추천합니다.
    /// 하이카드는 4플러시, 4스트레이트, 높은 카드 2장 유지 순으로 판단합니다.
    /// </summary>
    private List<int> CreateRecommendedExchangeIndexes(
        IList<int> cardNumbers)
    {
        List<int> result = new List<int>();

        if (cardNumbers == null ||
            cardNumbers.Count != 5)
        {
            return result;
        }

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            if (!CardUtility.IsValidCardNumber(
                    cardNumbers[i]))
            {
                return result;
            }
        }

        PokerHandValue handValue =
            PokerHandEvaluator.Evaluate(cardNumbers);

        Dictionary<int, int> rankCounts =
            CreateRankCountMap(cardNumbers);

        switch (handValue.Category)
        {
            case PokerHandCategory.StraightFlush:
            case PokerHandCategory.FourOfAKind:
            case PokerHandCategory.FullHouse:
            case PokerHandCategory.Flush:
            case PokerHandCategory.Straight:
                // 이미 강한 완성패이므로 추천 교환 없음.
                break;

            case PokerHandCategory.ThreeOfAKind:
                AddIndexesWhoseRankCountIsNot(
                    result,
                    cardNumbers,
                    rankCounts,
                    3
                );
                break;

            case PokerHandCategory.TwoPair:
                AddIndexesWhoseRankCountIs(
                    result,
                    cardNumbers,
                    rankCounts,
                    1
                );
                break;

            case PokerHandCategory.OnePair:
                AddIndexesWhoseRankCountIsNot(
                    result,
                    cardNumbers,
                    rankCounts,
                    2
                );
                break;

            case PokerHandCategory.HighCard:
                result =
                    CreateHighCardExchangeRecommendation(
                        cardNumbers
                    );
                break;
        }

        return LimitAndSortRecommendedIndexes(
            result,
            cardNumbers
        );
    }

    private Dictionary<int, int> CreateRankCountMap(
        IList<int> cardNumbers)
    {
        Dictionary<int, int> rankCounts =
            new Dictionary<int, int>();

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            int rank = GetPokerRank(cardNumbers[i]);

            if (!rankCounts.ContainsKey(rank))
            {
                rankCounts.Add(rank, 0);
            }

            rankCounts[rank]++;
        }

        return rankCounts;
    }

    private void AddIndexesWhoseRankCountIs(
        List<int> targetIndexes,
        IList<int> cardNumbers,
        Dictionary<int, int> rankCounts,
        int targetCount)
    {
        for (int i = 0; i < cardNumbers.Count; i++)
        {
            int rank = GetPokerRank(cardNumbers[i]);

            if (rankCounts[rank] == targetCount)
            {
                targetIndexes.Add(i);
            }
        }
    }

    private void AddIndexesWhoseRankCountIsNot(
        List<int> targetIndexes,
        IList<int> cardNumbers,
        Dictionary<int, int> rankCounts,
        int protectedCount)
    {
        for (int i = 0; i < cardNumbers.Count; i++)
        {
            int rank = GetPokerRank(cardNumbers[i]);

            if (rankCounts[rank] != protectedCount)
            {
                targetIndexes.Add(i);
            }
        }
    }

    /// <summary>
    /// 하이카드 추천 우선순위:
    /// 1. 같은 무늬 4장 유지
    /// 2. 한 장으로 스트레이트가 완성되는 4장 유지
    /// 3. 높은 카드 2장을 유지하고 나머지 3장 교환
    /// </summary>
    private List<int> CreateHighCardExchangeRecommendation(
        IList<int> cardNumbers)
    {
        List<int> result = new List<int>();

        int fourFlushDiscardIndex =
            FindFourCardFlushDiscardIndex(cardNumbers);

        if (fourFlushDiscardIndex >= 0)
        {
            result.Add(fourFlushDiscardIndex);
            return result;
        }

        int fourStraightDiscardIndex =
            FindBestFourCardStraightDiscardIndex(
                cardNumbers
            );

        if (fourStraightDiscardIndex >= 0)
        {
            result.Add(fourStraightDiscardIndex);
            return result;
        }

        // 한 번에 최대 3장 교환 규칙이므로 높은 카드 2장을 남깁니다.
        List<int> indexesByHighRank =
            new List<int>();

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            indexesByHighRank.Add(i);
        }

        indexesByHighRank.Sort(
            delegate (int left, int right)
            {
                int rankCompare =
                    GetPokerRank(cardNumbers[right])
                    .CompareTo(
                        GetPokerRank(cardNumbers[left])
                    );

                if (rankCompare != 0)
                {
                    return rankCompare;
                }

                return ((int)CardUtility.GetSuit(
                            cardNumbers[right]))
                    .CompareTo(
                        (int)CardUtility.GetSuit(
                            cardNumbers[left])
                    );
            }
        );

        HashSet<int> keptIndexes =
            new HashSet<int>();

        int keepCount = Math.Min(
            2,
            indexesByHighRank.Count
        );

        for (int i = 0; i < keepCount; i++)
        {
            keptIndexes.Add(indexesByHighRank[i]);
        }

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            if (!keptIndexes.Contains(i))
            {
                result.Add(i);
            }
        }

        return result;
    }

    private int FindFourCardFlushDiscardIndex(
        IList<int> cardNumbers)
    {
        int[] suitCounts = new int[4];

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            int suit =
                (int)CardUtility.GetSuit(cardNumbers[i]);

            suitCounts[suit]++;
        }

        int fourCardSuit = -1;

        for (int suit = 0; suit < suitCounts.Length; suit++)
        {
            if (suitCounts[suit] == 4)
            {
                fourCardSuit = suit;
                break;
            }
        }

        if (fourCardSuit < 0)
        {
            return -1;
        }

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            if ((int)CardUtility.GetSuit(cardNumbers[i]) !=
                fourCardSuit)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 카드 한 장을 제외했을 때 남은 4장이 하나 이상의 스트레이트 후보에 포함되면
    /// 해당 제외 인덱스를 반환합니다. 양쪽으로 완성 가능한 후보를 우선하고,
    /// 동률이면 더 높은 스트레이트를 만들 수 있는 후보를 선택합니다.
    /// </summary>
    private int FindBestFourCardStraightDiscardIndex(
        IList<int> cardNumbers)
    {
        int bestDiscardIndex = -1;
        int bestCompletionCount = -1;
        int bestStraightHighRank = -1;
        int bestKeptRankSum = -1;

        for (int discardIndex = 0;
             discardIndex < cardNumbers.Count;
             discardIndex++)
        {
            HashSet<int> keptRanks =
                new HashSet<int>();

            int keptRankSum = 0;

            for (int i = 0; i < cardNumbers.Count; i++)
            {
                if (i == discardIndex)
                {
                    continue;
                }

                int rank = GetPokerRank(cardNumbers[i]);
                keptRanks.Add(rank);
                keptRankSum += rank;
            }

            if (keptRanks.Count != 4)
            {
                continue;
            }

            HashSet<int> completionRanks =
                new HashSet<int>();

            int highestPossibleStraight = -1;

            for (int straightHigh = 5;
                 straightHigh <= 14;
                 straightHigh++)
            {
                HashSet<int> straightRanks =
                    CreateStraightRankSet(straightHigh);

                bool containsAllKeptRanks = true;

                foreach (int keptRank in keptRanks)
                {
                    if (!straightRanks.Contains(keptRank))
                    {
                        containsAllKeptRanks = false;
                        break;
                    }
                }

                if (!containsAllKeptRanks)
                {
                    continue;
                }

                foreach (int straightRank in straightRanks)
                {
                    if (!keptRanks.Contains(straightRank))
                    {
                        completionRanks.Add(straightRank);
                    }
                }

                highestPossibleStraight = Math.Max(
                    highestPossibleStraight,
                    straightHigh
                );
            }

            int completionCount = completionRanks.Count;

            if (completionCount <= 0)
            {
                continue;
            }

            bool isBetter =
                completionCount > bestCompletionCount ||
                (completionCount == bestCompletionCount &&
                 highestPossibleStraight > bestStraightHighRank) ||
                (completionCount == bestCompletionCount &&
                 highestPossibleStraight == bestStraightHighRank &&
                 keptRankSum > bestKeptRankSum);

            if (!isBetter)
            {
                continue;
            }

            bestDiscardIndex = discardIndex;
            bestCompletionCount = completionCount;
            bestStraightHighRank = highestPossibleStraight;
            bestKeptRankSum = keptRankSum;
        }

        return bestDiscardIndex;
    }

    private HashSet<int> CreateStraightRankSet(
        int straightHighRank)
    {
        HashSet<int> result = new HashSet<int>();

        if (straightHighRank == 5)
        {
            result.Add(14);
            result.Add(2);
            result.Add(3);
            result.Add(4);
            result.Add(5);
            return result;
        }

        for (int rank = straightHighRank - 4;
             rank <= straightHighRank;
             rank++)
        {
            result.Add(rank);
        }

        return result;
    }

    private List<int> LimitAndSortRecommendedIndexes(
        List<int> candidateIndexes,
        IList<int> cardNumbers)
    {
        List<int> validIndexes = new List<int>();

        if (candidateIndexes == null)
        {
            return validIndexes;
        }

        for (int i = 0; i < candidateIndexes.Count; i++)
        {
            int index = candidateIndexes[i];

            if (index < 0 ||
                index >= cardNumbers.Count ||
                validIndexes.Contains(index))
            {
                continue;
            }

            validIndexes.Add(index);
        }

        // 교환 한도가 추천 수보다 작을 때는 낮은 카드부터 우선 교환합니다.
        validIndexes.Sort(
            delegate (int left, int right)
            {
                int rankCompare =
                    GetPokerRank(cardNumbers[left])
                    .CompareTo(
                        GetPokerRank(cardNumbers[right])
                    );

                if (rankCompare != 0)
                {
                    return rankCompare;
                }

                return left.CompareTo(right);
            }
        );

        if (validIndexes.Count > maxExchangeCards)
        {
            validIndexes.RemoveRange(
                maxExchangeCards,
                validIndexes.Count - maxExchangeCards
            );
        }

        // 실제 교환은 손패 슬롯 순으로 처리해 연출 순서를 안정적으로 유지합니다.
        validIndexes.Sort();

        return validIndexes;
    }

    private int GetPokerRank(int cardNumber)
    {
        return (int)CardUtility.GetRank(cardNumber) + 2;
    }

    private string CreateExchangeRecommendationLog(
        PlayerControl player,
        IList<int> recommendedIndexes)
    {
        if (recommendedIndexes == null ||
            recommendedIndexes.Count == 0)
        {
            return "Player " +
                   player.playerNumber +
                   " 추천 교환: 없음";
        }

        string cardNames = string.Empty;

        for (int i = 0;
             i < recommendedIndexes.Count;
             i++)
        {
            int handIndex = recommendedIndexes[i];

            if (handIndex < 0 ||
                handIndex >= player.cardNumbers.Count)
            {
                continue;
            }

            if (cardNames.Length > 0)
            {
                cardNames += ", ";
            }

            cardNames += CardUtility.GetCardName(
                player.cardNumbers[handIndex]
            );
        }

        return "Player " +
               player.playerNumber +
               " 추천 교환: " +
               cardNames;
    }

    #endregion

    #region Phase Transition Delay

    private void StartExchangePhase()
    {
        BeginDelayedPhaseTransition(
            GamePhase.Exchange,
            firstBettingToExchangeDelay
        );
    }

    private void StartFinalBettingPhase()
    {
        BeginDelayedPhaseTransition(
            GamePhase.FinalBetting,
            exchangeToFinalBettingDelay
        );
    }

    private void StartShowdownPhase()
    {
        BeginDelayedPhaseTransition(
            GamePhase.Showdown,
            finalBettingToShowdownDelay
        );
    }

    private void BeginDelayedPhaseTransition(
        GamePhase targetPhase,
        float delay)
    {
        if (isPhaseTransitioning ||
            isWaitingForWinnerDisplay ||
            isGameFinished)
        {
            return;
        }

        // 다음 페이즈가 시작되기 전에는 누구도 추가 행동을 할 수 없도록
        // 현재 턴을 비우고 버튼을 비활성화합니다.
        SetCurrentTurn(-1);

        if (delay <= 0f)
        {
            ExecutePhaseTransition(targetPhase);
            return;
        }

        isPhaseTransitioning = true;

        phaseTransitionCoroutine = StartCoroutine(
            PhaseTransitionRoutine(
                targetPhase,
                delay
            )
        );
    }

    private IEnumerator PhaseTransitionRoutine(
        GamePhase targetPhase,
        float delay)
    {
        yield return new WaitForSeconds(delay);

        phaseTransitionCoroutine = null;
        isPhaseTransitioning = false;

        ExecutePhaseTransition(targetPhase);
    }

    private void ExecutePhaseTransition(
        GamePhase targetPhase)
    {
        phaseTransitionCoroutine = null;
        isPhaseTransitioning = false;

        switch (targetPhase)
        {
            case GamePhase.Exchange:
                StartExchangePhaseImmediate();
                break;

            case GamePhase.FinalBetting:
                StartFinalBettingPhaseImmediate();
                break;

            case GamePhase.Showdown:
                StartShowdownPhaseImmediate();
                break;
        }
    }

    #endregion

    #region Betting Phase Start

    private void StartFirstBettingPhase()
    {
        ClearAllBettingActionIcons();

        currentPhase = GamePhase.FirstBetting;

        for (int i = 0; i < players.Count; i++)
        {
            if (!IsPlayerSeated(players[i]))
            {
                continue;
            }

            // SB와 BB 금액은 유지하고 행동 여부만 초기화합니다.
            players[i].PrepareForBettingRound(false);
        }

        currentHighestBet = 0L;

        for (int i = 0; i < players.Count; i++)
        {
            if (!IsPlayerSeated(players[i]))
            {
                continue;
            }

            currentHighestBet = Math.Max(
                currentHighestBet,
                players[i].RoundBetMoney
            );
        }

        lastRaiseIncrement = GetBaseMinimumBet();

        // 첫 번째 베팅은 BB 왼쪽부터 시작합니다.
        int firstIndex =
            FindNextPlayerNeedingBettingAction(bigBlindIndex);

        if (firstIndex < 0)
        {
            StartExchangePhase();
            return;
        }

        SetCurrentTurn(firstIndex);
    }

    private void StartFinalBettingPhaseImmediate()
    {
        ClearHumanExchangeReservation(true);
        ClearAllBettingActionIcons();

        currentPhase = GamePhase.FinalBetting;
        currentHighestBet = 0L;
        lastRaiseIncrement = GetBaseMinimumBet();

        for (int i = 0; i < players.Count; i++)
        {
            if (!IsPlayerSeated(players[i]))
            {
                continue;
            }

            // 두 번째 베팅 라운드이므로 라운드 베팅액은 0으로 초기화합니다.
            players[i].PrepareForBettingRound(true);
        }

        if (GetActivePlayerCount() <= 1)
        {
            FinishWithSinglePlayer();
            return;
        }

        // 한 명만 베팅할 수 있고 나머지가 올인이라면 바로 쇼다운합니다.
        if (GetPlayersAbleToBetCount() <= 1)
        {
            StartShowdownPhase();
            return;
        }

        // 마지막 베팅은 딜러 왼쪽부터 시작합니다.
        int firstIndex =
            FindNextPlayerNeedingBettingAction(dealerIndex);

        if (firstIndex < 0)
        {
            StartShowdownPhase();
            return;
        }

        SetCurrentTurn(firstIndex);
    }

    #endregion

    #region Betting Actions

    public void SubmitBettingAction(
        PlayerControl player,
        BettingAction action)
    {
        if (!CanSubmitBettingAction(player))
        {
            return;
        }

        // UI의 interactable 상태와 실제 게임 규칙이 항상 같도록
        // 행동을 실행하기 직전에 동일한 가능 여부 검사를 다시 수행합니다.
        if (!IsBettingActionAvailable(
                player,
                action,
                false))
        {
            Debug.LogWarning(
                "현재 상태에서는 " +
                GetBettingActionName(action) +
                " 행동을 사용할 수 없습니다."
            );

            RefreshGameUI();
            return;
        }

        switch (action)
        {
            case BettingAction.Fold:
                ProcessFold(player);
                break;

            case BettingAction.Check:
                ProcessCheck(player);
                break;

            case BettingAction.Call:
                ProcessCall(player);
                break;

            case BettingAction.Ping:
                ProcessRaiseTo(
                    player,
                    CalculatePingTarget(player),
                    action
                );
                break;

            case BettingAction.Double:
                ProcessRaiseTo(
                    player,
                    CalculateDoubleTarget(player),
                    action
                );
                break;

            case BettingAction.Quarter:
                ProcessRaiseTo(
                    player,
                    CalculateQuarterTarget(player),
                    action
                );
                break;

            case BettingAction.Half:
                ProcessRaiseTo(
                    player,
                    CalculateHalfTarget(player),
                    action
                );
                break;

            case BettingAction.AllIn:
                ProcessRaiseTo(
                    player,
                    player.RoundBetMoney + player.CurrentMoney,
                    action
                );
                break;

            case BettingAction.Max:
                ProcessRaiseTo(
                    player,
                    CalculateMaxTarget(player),
                    action
                );
                break;
        }
    }

    /// <summary>
    /// 지정 플레이어가 현재 행동을 실제로 실행할 수 있는지 반환합니다.
    /// 토글 interactable과 SubmitBettingAction이 같은 판정을 공유합니다.
    /// </summary>
    public bool IsBettingActionAvailable(
        PlayerControl player,
        BettingAction action)
    {
        return IsBettingActionAvailable(
            player,
            action,
            true
        );
    }

    private bool IsBettingActionAvailable(
        PlayerControl player,
        BettingAction action,
        bool checkTurnContext)
    {
        if (isHomeScreenOpen ||
            !isSessionActive ||
            !IsPlayerSeated(player))
        {
            return false;
        }

        // 상대 턴에도 미리 선택할 수 있어야 하므로 UI 판정에서는
        // 턴만 무시하고, 베팅 페이즈와 실제 금액 조건은 그대로 검사합니다.
        if (currentPhase != GamePhase.FirstBetting &&
            currentPhase != GamePhase.FinalBetting)
        {
            return false;
        }

        if (checkTurnContext &&
            CurrentTurnPlayer != player)
        {
            return false;
        }

        if (!CanPlayerBet(player))
        {
            return false;
        }

        // 이미 이번 베팅 라운드에서 행동을 끝냈고 현재 최고 베팅액까지
        // 맞춘 플레이어에게는 추가 행동권이 없습니다.
        // 따라서 상대들이 콜 또는 다이만 하는 동안에는 예약 토글을 포함한
        // 모든 베팅 토글을 비활성화합니다.
        // 이후 다른 플레이어가 레이즈하면 ResetOtherPlayerActions()에서
        // HasActedThisBettingRound가 false로 되므로 다시 예약할 수 있습니다.
        if (!NeedsBettingAction(player))
        {
            return false;
        }

        long callAmount =
            GetCurrentCallAmount(player);

        switch (action)
        {
            case BettingAction.Fold:
                return true;

            case BettingAction.Check:
                return callAmount <= 0L;

            case BettingAction.Call:
                // 콜 금액보다 잔액이 적어도 남은 금액 전부를 내는
                // 숏 올인 콜은 허용합니다.
                return callAmount > 0L &&
                       GetPayableCallAmount(player) > 0L;

            case BettingAction.Ping:
                {
                    long resolvedTarget;

                    // 한게임처럼 현재 내가 콜할 금액이 없을 때 삥을 표시합니다.
                    // 블라인드가 이미 올라가 있어 currentHighestBet이 0이 아니어도
                    // 내 베팅액이 최고 베팅액과 같다면 삥을 사용할 수 있습니다.
                    return callAmount <= 0L &&
                           TryResolveLegalRaiseTarget(
                               player,
                               CalculatePingTarget(player),
                               action,
                               out resolvedTarget
                           );
                }

            case BettingAction.Double:
                {
                    long resolvedTarget;

                    // 한게임처럼 콜할 금액이 있을 때 삥 대신 따당을 표시합니다.
                    return callAmount > 0L &&
                           TryResolveLegalRaiseTarget(
                               player,
                               CalculateDoubleTarget(player),
                               action,
                               out resolvedTarget
                           );
                }

            case BettingAction.Quarter:
                {
                    long resolvedTarget;

                    return TryResolveLegalRaiseTarget(
                        player,
                        CalculateQuarterTarget(player),
                        action,
                        out resolvedTarget
                    );
                }

            case BettingAction.Half:
                {
                    long resolvedTarget;

                    return TryResolveLegalRaiseTarget(
                        player,
                        CalculateHalfTarget(player),
                        action,
                        out resolvedTarget
                    );
                }

            case BettingAction.AllIn:
                // 자발적인 올인은 카드 교환이 끝난 마지막 베팅에서만 허용합니다.
                // 첫 번째 베팅에서 잔액이 콜 금액보다 부족한 경우에는
                // AllIn 행동이 아니라 Call 행동으로 남은 돈 전부를 내는 숏 올인 콜이 가능합니다.
                return IsVoluntaryAllInOrMaxAllowed() &&
                       ShouldShowAllInToggle(player) &&
                       player.CurrentMoney > 0L;

            case BettingAction.Max:
                {
                    long resolvedTarget;

                    // 맥스도 카드 교환이 끝난 마지막 베팅에서만 허용합니다.
                    return IsVoluntaryAllInOrMaxAllowed() &&
                           !ShouldShowAllInToggle(player) &&
                           TryResolveLegalRaiseTarget(
                               player,
                               CalculateMaxTarget(player),
                               action,
                               out resolvedTarget
                           );
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Player 0의 Toggle OnValueChanged(bool)에서 호출합니다.
    /// 상대 턴이면 행동을 예약하고, 내 턴이면 즉시 실행합니다.
    /// </summary>
    public void HandleHumanBettingToggleChanged(
        PlayerControl player,
        BettingAction action,
        bool isOn)
    {
        if (player == null ||
            !player.IsHumanPlayer ||
            player.playerNumber != 0)
        {
            return;
        }

        if (!isOn)
        {
            if (reservedHumanBettingAction == (int)action)
            {
                ClearHumanBettingReservation(false);
            }

            return;
        }

        // UI가 갱신되기 직전 상태가 바뀌었을 수 있으므로
        // 예약할 때도 현재 금액 조건을 다시 검사합니다.
        if (!IsBettingActionAvailable(
                player,
                action,
                false))
        {
            if (reservedHumanBettingAction == (int)action)
            {
                ClearHumanBettingReservation(false);
            }

            Toggle actionToggle =
                GetBettingToggle(action);

            if (actionToggle != null)
            {
                actionToggle.SetIsOnWithoutNotify(false);
            }

            RefreshHumanBettingToggleUI();
            return;
        }

        reservedHumanBettingAction =
            (int)action;

        // 내 턴에 직접 선택한 경우에는 예약 대기 없이 즉시 실행합니다.
        if (CurrentTurnPlayer == player)
        {
            TryExecuteReservedHumanBettingAction();
        }
    }

    /// <summary>
    /// 상대 턴에 미리 선택한 예약 베팅을 내 턴이 된 뒤 지정된 시간만큼 기다렸다가 실행합니다.
    /// 예약 대기 중 토글을 해제하거나 상황이 바뀌면 실행하지 않습니다.
    /// </summary>
    private bool TryScheduleReservedHumanBettingAction()
    {
        if (isExecutingReservedHumanBettingAction ||
            reservedHumanBettingAction < 0)
        {
            return false;
        }

        PlayerControl humanPlayer =
            GetPlayerByNumber(0);

        if (humanPlayer == null ||
            CurrentTurnPlayer != humanPlayer)
        {
            return false;
        }

        if (!IsBettingActionAvailable(
                humanPlayer,
                (BettingAction)reservedHumanBettingAction,
                true))
        {
            ClearHumanBettingReservation(true);
            RefreshHumanBettingToggleUI();
            return false;
        }

        CancelReservedHumanBettingDelay();

        if (reservedHumanBettingExecutionDelay <= 0f)
        {
            return TryExecuteReservedHumanBettingAction();
        }

        reservedHumanBettingDelayCoroutine =
            StartCoroutine(
                ExecuteReservedHumanBettingAfterDelay()
            );

        return true;
    }

    private IEnumerator ExecuteReservedHumanBettingAfterDelay()
    {
        int scheduledAction =
            reservedHumanBettingAction;

        yield return new WaitForSeconds(
            reservedHumanBettingExecutionDelay
        );

        reservedHumanBettingDelayCoroutine = null;

        // 대기 중 예약이 취소되거나 다른 행동으로 바뀌었다면 실행하지 않습니다.
        if (reservedHumanBettingAction != scheduledAction)
        {
            yield break;
        }

        TryExecuteReservedHumanBettingAction();
    }

    private void CancelReservedHumanBettingDelay()
    {
        if (reservedHumanBettingDelayCoroutine == null)
        {
            return;
        }

        StopCoroutine(reservedHumanBettingDelayCoroutine);
        reservedHumanBettingDelayCoroutine = null;
    }

    /// <summary>
    /// 현재 예약된 Player 0 행동을 실제로 한 번 실행합니다.
    /// 실행 직전에 조건이 달라졌다면 예약만 취소합니다.
    /// </summary>
    private bool TryExecuteReservedHumanBettingAction()
    {
        if (isExecutingReservedHumanBettingAction ||
            reservedHumanBettingAction < 0)
        {
            return false;
        }

        PlayerControl humanPlayer =
            GetPlayerByNumber(0);

        if (humanPlayer == null ||
            CurrentTurnPlayer != humanPlayer)
        {
            return false;
        }

        BettingAction action =
            (BettingAction)reservedHumanBettingAction;

        if (!IsBettingActionAvailable(
                humanPlayer,
                action,
                true))
        {
            ClearHumanBettingReservation(true);
            RefreshHumanBettingToggleUI();
            return false;
        }

        // SubmitBettingAction 안에서 다음 턴으로 넘어가며 UI가 재갱신되므로
        // 중복 실행되지 않도록 예약을 먼저 지웁니다.
        ClearHumanBettingReservation(true);
        isExecutingReservedHumanBettingAction = true;

        try
        {
            SubmitBettingAction(
                humanPlayer,
                action
            );
        }
        finally
        {
            isExecutingReservedHumanBettingAction = false;
        }

        return true;
    }

    private void ClearHumanBettingReservation(
        bool turnOffToggle)
    {
        CancelReservedHumanBettingDelay();

        if (turnOffToggle &&
            reservedHumanBettingAction >= 0)
        {
            Toggle reservedToggle =
                GetBettingToggle(
                    (BettingAction)reservedHumanBettingAction
                );

            if (reservedToggle != null)
            {
                reservedToggle.SetIsOnWithoutNotify(false);
            }
        }

        reservedHumanBettingAction = -1;
    }

    private Toggle GetBettingToggle(
        BettingAction action)
    {
        switch (action)
        {
            case BettingAction.Fold:
                return foldToggle;
            case BettingAction.Ping:
                return pingToggle;
            case BettingAction.Double:
                return doubleToggle;
            case BettingAction.Call:
                return callToggle;
            case BettingAction.Check:
                return checkToggle;
            case BettingAction.Quarter:
                return quarterToggle;
            case BettingAction.Half:
                return halfToggle;
            case BettingAction.AllIn:
                return allInToggle;
            case BettingAction.Max:
                return maxToggle;
            default:
                return null;
        }
    }


    /// <summary>
    /// 교환 페이즈에서 Player 0이 패스 또는 카드 교환을 선택할 수 있는지 반환합니다.
    /// UI 예약 판정에서는 턴을 무시하고, 실제 실행 직전에는 현재 턴까지 검사합니다.
    /// </summary>
    public bool IsHumanExchangeActionAvailable(
        PlayerControl player,
        ExchangeAction action)
    {
        return IsHumanExchangeActionAvailable(
            player,
            action,
            true
        );
    }

    private bool IsHumanExchangeActionAvailable(
        PlayerControl player,
        ExchangeAction action,
        bool checkTurnContext)
    {
        if (isHomeScreenOpen ||
            !isSessionActive ||
            !IsPlayerSeated(player) ||
            !player.IsHumanPlayer ||
            player.playerNumber != 0)
        {
            return false;
        }

        if (currentPhase != GamePhase.Exchange)
        {
            return false;
        }

        if (checkTurnContext &&
            CurrentTurnPlayer != player)
        {
            return false;
        }

        if (player.IsFolded ||
            player.HasExchangedThisGame ||
            !player.HasValidFiveCardHand() ||
            IsPlayerCurrentlyExchanging(player.playerNumber))
        {
            return false;
        }

        switch (action)
        {
            case ExchangeAction.Pass:
                return true;

            case ExchangeAction.Exchange:
                return player.SelectedExchangeCardCount > 0;

            default:
                return false;
        }
    }

    /// <summary>
    /// Player 0의 교환 Toggle OnValueChanged(bool)에서 호출합니다.
    /// 상대 교환 차례에는 행동을 예약하고, 내 차례라면 즉시 실행합니다.
    /// </summary>
    public void HandleHumanExchangeToggleChanged(
        PlayerControl player,
        ExchangeAction action,
        bool isOn)
    {
        if (player == null ||
            !player.IsHumanPlayer ||
            player.playerNumber != 0)
        {
            return;
        }

        if (!isOn)
        {
            if (reservedHumanExchangeAction == (int)action)
            {
                reservedHumanExchangeAction = -1;
            }

            return;
        }

        // 패스를 선택하면 카드 선택 표시가 남지 않도록 선택 카드를 모두 해제합니다.
        if (action == ExchangeAction.Pass &&
            player.selectedExchangeIndexes.Count > 0)
        {
            player.selectedExchangeIndexes.Clear();
        }

        if (!IsHumanExchangeActionAvailable(
                player,
                action,
                false))
        {
            if (reservedHumanExchangeAction == (int)action)
            {
                reservedHumanExchangeAction = -1;
            }

            Toggle actionToggle =
                GetExchangeToggle(action);

            if (actionToggle != null)
            {
                actionToggle.SetIsOnWithoutNotify(false);
            }

            RefreshHumanExchangeToggleUI();
            return;
        }

        reservedHumanExchangeAction =
            (int)action;

        RefreshHumanExchangeToggleUI();

        // 내 교환 차례에 직접 선택했다면 예약 대기 없이 즉시 실행합니다.
        if (CurrentTurnPlayer == player)
        {
            TryExecuteReservedHumanExchangeAction();
        }
    }

    /// <summary>
    /// Player 0의 카드 선택 개수가 바뀔 때 호출합니다.
    /// 패스 예약 후 카드를 선택하면 패스를 취소하고,
    /// 교환 예약 후 선택 카드가 0장이 되면 교환 예약을 취소합니다.
    /// </summary>
    public void NotifyHumanExchangeSelectionChanged(
        PlayerControl player)
    {
        if (player == null ||
            !player.IsHumanPlayer ||
            player.playerNumber != 0)
        {
            return;
        }

        if (reservedHumanExchangeAction ==
                (int)ExchangeAction.Pass &&
            player.SelectedExchangeCardCount > 0)
        {
            ClearHumanExchangeReservation(true);
        }
        else if (reservedHumanExchangeAction ==
                     (int)ExchangeAction.Exchange &&
                 player.SelectedExchangeCardCount <= 0)
        {
            ClearHumanExchangeReservation(true);
        }

        RefreshHumanExchangeToggleUI();
    }

    /// <summary>
    /// 예약된 패스 또는 카드 교환을 Player 0의 차례가 되는 순간 한 번 실행합니다.
    /// </summary>
    private bool TryExecuteReservedHumanExchangeAction()
    {
        if (isExecutingReservedHumanExchangeAction ||
            reservedHumanExchangeAction < 0)
        {
            return false;
        }

        PlayerControl humanPlayer =
            GetPlayerByNumber(0);

        if (humanPlayer == null ||
            CurrentTurnPlayer != humanPlayer)
        {
            return false;
        }

        ExchangeAction action =
            (ExchangeAction)reservedHumanExchangeAction;

        if (!IsHumanExchangeActionAvailable(
                humanPlayer,
                action,
                true))
        {
            ClearHumanExchangeReservation(true);
            RefreshHumanExchangeToggleUI();
            return false;
        }

        List<int> selectedIndexes =
            action == ExchangeAction.Exchange
                ? new List<int>(
                    humanPlayer.selectedExchangeIndexes
                )
                : new List<int>();

        // 패스는 선택 카드가 남아 있더라도 실제 교환 없이 진행합니다.
        if (action == ExchangeAction.Pass)
        {
            humanPlayer.selectedExchangeIndexes.Clear();
        }

        // SubmitExchange에서 교환 코루틴이 시작되기 전에 예약을 먼저 지워
        // 같은 행동이 중복 실행되지 않도록 합니다.
        ClearHumanExchangeReservation(true);
        isExecutingReservedHumanExchangeAction = true;

        try
        {
            SubmitExchange(
                humanPlayer,
                selectedIndexes
            );
        }
        finally
        {
            isExecutingReservedHumanExchangeAction = false;
        }

        return true;
    }

    private void ClearHumanExchangeReservation(
        bool turnOffToggle)
    {
        if (turnOffToggle &&
            reservedHumanExchangeAction >= 0)
        {
            Toggle reservedToggle =
                GetExchangeToggle(
                    (ExchangeAction)reservedHumanExchangeAction
                );

            if (reservedToggle != null)
            {
                reservedToggle.SetIsOnWithoutNotify(false);
            }
        }

        reservedHumanExchangeAction = -1;
    }

    private Toggle GetExchangeToggle(
        ExchangeAction action)
    {
        switch (action)
        {
            case ExchangeAction.Pass:
                return passToggle;

            case ExchangeAction.Exchange:
                return exchangeToggle;

            default:
                return null;
        }
    }

    private bool CanSubmitBettingAction(
        PlayerControl player,
        bool showWarning = true)
    {
        if (isHomeScreenOpen ||
            !isSessionActive ||
            !IsPlayerSeated(player))
        {
            return false;
        }

        if (currentPhase != GamePhase.FirstBetting &&
            currentPhase != GamePhase.FinalBetting)
        {
            if (showWarning)
            {
                Debug.LogWarning(
                    "현재는 베팅 페이즈가 아닙니다."
                );
            }

            return false;
        }

        if (CurrentTurnPlayer != player)
        {
            if (showWarning)
            {
                Debug.LogWarning(
                    "현재 턴인 플레이어만 행동할 수 있습니다."
                );
            }

            return false;
        }

        if (!CanPlayerBet(player))
        {
            return false;
        }

        return true;
    }

    private void ProcessFold(PlayerControl player)
    {
        long callBefore = GetCurrentCallAmount(player);
        long potBefore = totalPot;
        long highestBefore = currentHighestBet;

        player.SetFolded(true);
        player.SetActedThisBettingRound(true);
        PlayPlayerActionVoice(player, BettingAction.Fold);
        ShowBettingActionIcon(player, BettingAction.Fold);

        RecordBettingHistory(
            player,
            BettingAction.Fold,
            0L,
            callBefore,
            potBefore,
            highestBefore
        );

        Debug.Log(
            "Player " + player.playerNumber + " 다이"
        );

        AdvanceAfterBettingAction();
    }

    private void ProcessCheck(PlayerControl player)
    {
        long callBefore = GetCurrentCallAmount(player);

        if (callBefore > 0L)
        {
            Debug.LogWarning(
                "콜해야 할 금액이 남아 있어 체크할 수 없습니다."
            );
            return;
        }

        long potBefore = totalPot;
        long highestBefore = currentHighestBet;

        player.SetActedThisBettingRound(true);
        PlayPlayerActionVoice(player, BettingAction.Check);
        ShowBettingActionIcon(player, BettingAction.Check);

        RecordBettingHistory(
            player,
            BettingAction.Check,
            0L,
            callBefore,
            potBefore,
            highestBefore
        );

        Debug.Log(
            "Player " + player.playerNumber + " 체크"
        );

        AdvanceAfterBettingAction();
    }

    private void ProcessCall(PlayerControl player)
    {
        long callAmount = GetCurrentCallAmount(player);

        if (callAmount <= 0L)
        {
            ProcessCheck(player);
            return;
        }

        long potBefore = totalPot;
        long highestBefore = currentHighestBet;

        long payableCallAmount =
            GetPayableCallAmount(player);

        long paidAmount =
            player.CommitBet(payableCallAmount);

        totalPot = SafeAdd(totalPot, paidAmount);

        PlayBetChipAnimation(player, paidAmount);

        player.SetActedThisBettingRound(true);
        PlayPlayerActionVoice(player, BettingAction.Call);
        ShowBettingActionIcon(player, BettingAction.Call);

        RecordBettingHistory(
            player,
            BettingAction.Call,
            paidAmount,
            callAmount,
            potBefore,
            highestBefore
        );

        Debug.Log(
            "Player " + player.playerNumber +
            " 콜 / 추가 " +
            PlayerControl.FormatKoreanMoney(paidAmount)
        );

        AdvanceAfterBettingAction();
    }

    private void ProcessCallOrCheck(PlayerControl player)
    {
        if (GetCurrentCallAmount(player) > 0L)
        {
            ProcessCall(player);
        }
        else
        {
            ProcessCheck(player);
        }
    }

    private void ProcessRaiseTo(
        PlayerControl player,
        long requestedTargetBet,
        BettingAction action)
    {
        long callBefore = GetCurrentCallAmount(player);
        long potBefore = totalPot;
        long highestBefore = currentHighestBet;

        long allInTarget =
            SafeAdd(
                player.RoundBetMoney,
                player.CurrentMoney
            );

        if (allInTarget <= player.RoundBetMoney)
        {
            ProcessCallOrCheck(player);
            return;
        }

        // 맥스는 베팅 라운드별 제한이 아니라 이번 판 전체 누적 제한입니다.
        // AllIn은 남은 전 재산이 판당 한도 안에 들어올 때만 노출되므로
        // 안전을 위해 모든 액션에 동일한 판당 한도를 적용합니다.
        long maximumAllowedTarget =
            CalculateMaximumRoundTargetByGameLimit(player);

        long targetBet = Math.Min(
            requestedTargetBet,
            maximumAllowedTarget
        );

        // 쿼터/하프의 6250 같은 끝자리 금액을 보존하기 위해
        // 베팅 단위 반올림과 일반 홀덤식 최소 레이즈 보정을 하지 않습니다.
        if (targetBet <= currentHighestBet)
        {
            ProcessCallOrCheck(player);
            return;
        }

        long oldHighestBet = currentHighestBet;
        long additionalAmount =
            targetBet - player.RoundBetMoney;

        long paidAmount =
            player.CommitBet(additionalAmount);

        totalPot = SafeAdd(totalPot, paidAmount);

        PlayBetChipAnimation(player, paidAmount);

        long newPlayerBet = player.RoundBetMoney;

        if (newPlayerBet > oldHighestBet)
        {
            long raiseIncrement =
                newPlayerBet - oldHighestBet;

            currentHighestBet = newPlayerBet;

            if (raiseIncrement > 0L)
            {
                lastRaiseIncrement = raiseIncrement;
            }

            ResetOtherPlayerActions(player);
        }

        player.SetActedThisBettingRound(true);
        PlayPlayerActionVoice(player, action);
        ShowBettingActionIcon(player, action);

        RecordBettingHistory(
            player,
            action,
            paidAmount,
            callBefore,
            potBefore,
            highestBefore
        );

        Debug.Log(
            "Player " + player.playerNumber +
            " " + GetBettingActionName(action) +
            " / 추가 " +
            PlayerControl.FormatKoreanMoney(paidAmount) +
            " / 라운드 누적 " +
            PlayerControl.FormatKoreanMoney(player.RoundBetMoney)
        );

        AdvanceAfterBettingAction();
    }


    /// <summary>
    /// 새 행동을 표시합니다. 직전의 최신 행동자는 지난 행동 스프라이트로 교체하고,
    /// 방금 행동한 플레이어만 현재 행동 스프라이트로 강조합니다.
    /// </summary>
    private void ShowBettingActionIcon(
        PlayerControl player,
        BettingAction action)
    {
        if (player == null)
        {
            return;
        }

        if (latestActionIconPlayer != null &&
            latestActionIconPlayer != player)
        {
            BettingAction previousAction;

            if (displayedBettingActions.TryGetValue(
                    latestActionIconPlayer,
                    out previousAction))
            {
                latestActionIconPlayer.SetBettingActionIcon(
                    ResolveBettingActionSprite(
                        previousTurnActionSprites,
                        currentTurnActionSprites,
                        previousAction
                    )
                );
            }
        }

        displayedBettingActions[player] = action;

        player.SetBettingActionIcon(
            ResolveBettingActionSprite(
                currentTurnActionSprites,
                previousTurnActionSprites,
                action
            )
        );

        latestActionIconPlayer = player;
    }

    /// <summary>
    /// 우선 세트에 해당 스프라이트가 없으면 반대 세트의 같은 행동 스프라이트를 사용합니다.
    /// 일부 항목을 아직 연결하지 않은 테스트 단계에서도 아이콘이 갑자기 사라지지 않게 합니다.
    /// </summary>
    private Sprite ResolveBettingActionSprite(
        BettingActionSpriteSet primarySet,
        BettingActionSpriteSet fallbackSet,
        BettingAction action)
    {
        Sprite result =
            primarySet != null
                ? primarySet.GetSprite(action)
                : null;

        if (result == null && fallbackSet != null)
        {
            result = fallbackSet.GetSprite(action);
        }

        return result;
    }

    /// <summary>
    /// 새 판, 교환 진입, 두 번째 베팅 시작, 쇼다운처럼 페이즈가 바뀔 때
    /// 모든 플레이어의 행동 아이콘과 저장된 행동 기록을 초기화합니다.
    /// </summary>
    private void ClearAllBettingActionIcons()
    {
        displayedBettingActions.Clear();
        latestActionIconPlayer = null;

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null)
            {
                players[i].ClearBettingActionIcon();
            }
        }
    }

    /// <summary>
    /// 실제로 플레이어 보유금액에서 빠져나간 금액만 칩 연출에 전달합니다.
    /// 다이/체크처럼 지불금액이 0인 행동은 칩을 생성하지 않습니다.
    /// </summary>
    private void PlayBetChipAnimation(
        PlayerControl player,
        long paidAmount)
    {
        if (player == null || paidAmount <= 0L)
        {
            return;
        }

        if (audioManager != null)
        {
            audioManager.PlayChipThrow();
        }

        if (chipBetAnimator != null)
        {
            chipBetAnimator.PlayBet(player, paidAmount);
        }
    }

    private void PlayPlayerActionVoice(
        PlayerControl player,
        BettingAction action)
    {
        if (audioManager != null)
        {
            audioManager.PlayPlayerAction(player, action);
        }
    }

    private void RecordBettingHistory(
        PlayerControl player,
        BettingAction action,
        long paidAmount,
        long callAmountBefore,
        long potBefore,
        long highestBetBefore)
    {
        if (aiHistoryTracker == null || player == null)
        {
            return;
        }

        aiHistoryTracker.RecordBettingAction(
            player,
            action,
            currentPhase,
            callAmountBefore,
            paidAmount,
            potBefore,
            highestBetBefore,
            gameNumber
        );
    }

    private void ResetOtherPlayerActions(PlayerControl raiser)
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl otherPlayer = players[i];

            if (otherPlayer == raiser)
            {
                continue;
            }

            if (!CanPlayerBet(otherPlayer))
            {
                continue;
            }

            otherPlayer.SetActedThisBettingRound(false);
        }
    }

    private void AdvanceAfterBettingAction()
    {
        if (GetActivePlayerCount() <= 1)
        {
            FinishWithSinglePlayer();
            return;
        }

        if (IsBettingRoundComplete())
        {
            if (currentPhase == GamePhase.FirstBetting)
            {
                StartExchangePhase();
            }
            else
            {
                StartShowdownPhase();
            }

            return;
        }

        int nextIndex =
            FindNextPlayerNeedingBettingAction(currentTurnIndex);

        if (nextIndex < 0)
        {
            if (currentPhase == GamePhase.FirstBetting)
            {
                StartExchangePhase();
            }
            else
            {
                StartShowdownPhase();
            }

            return;
        }

        SetCurrentTurn(nextIndex);
    }

    private bool IsBettingRoundComplete()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!CanPlayerBet(player))
            {
                continue;
            }

            if (!player.HasActedThisBettingRound)
            {
                return false;
            }

            if (player.RoundBetMoney != currentHighestBet)
            {
                return false;
            }
        }

        return true;
    }

    private bool NeedsBettingAction(PlayerControl player)
    {
        if (!CanPlayerBet(player))
        {
            return false;
        }

        if (!player.HasActedThisBettingRound)
        {
            return true;
        }

        return player.RoundBetMoney != currentHighestBet;
    }

    private bool CanPlayerBet(PlayerControl player)
    {
        return !isHomeScreenOpen &&
               isSessionActive &&
               IsPlayerSeated(player) &&
               !player.IsFolded &&
               !player.IsAllIn &&
               !HasReachedBetLimitThisGame(player);
    }

    #endregion

    #region Bet Amount Calculation

    /// <summary>
    /// 한게임식 삥 표시 금액입니다.
    /// 팟 크기와 무관하게 방의 기본 베팅액을 사용합니다.
    /// </summary>
    private long CalculatePingBetAmount()
    {
        long amount =
            pingBetAmount > 0L
                ? pingBetAmount
                : bigBlindAmount;

        return Math.Max(
            1L,
            RoundUpToBetUnit(amount)
        );
    }

    private long CalculatePingTarget(
        PlayerControl player)
    {
        if (player == null)
        {
            return 0L;
        }

        // 삥 버튼 위에는 추가로 내는 기본 베팅액을 표시하고,
        // 실제 목표액은 현재 내 라운드 베팅액에 그 금액을 더합니다.
        return SafeAdd(
            player.RoundBetMoney,
            CalculatePingBetAmount()
        );
    }

    /// <summary>
    /// 한게임식 따당 금액입니다.
    /// 현재 콜 금액, 즉 앞사람의 유효 베팅 금액을 2배로 표시합니다.
    /// </summary>
    private long CalculateDoubleBetAmount(
        PlayerControl player)
    {
        long callAmount =
            GetCurrentCallAmount(player);

        return SafeMultiply(callAmount, 2L);
    }

    private long CalculateDoubleTarget(
        PlayerControl player)
    {
        if (player == null)
        {
            return 0L;
        }

        // 따당 표시 금액 전체를 이번 행동에서 추가 납부합니다.
        // 예: 콜 197만 6000 -> 따당 표시/추가금 395만 2000.
        return SafeAdd(
            player.RoundBetMoney,
            CalculateDoubleBetAmount(player)
        );
    }

    /// <summary>
    /// 한게임식 쿼터 표시 금액입니다.
    /// 현재 팟에 내 콜 금액을 먼저 더한 뒤 1/4을 계산합니다.
    /// 단위 반올림을 하지 않아 169만 6250 같은 금액을 그대로 유지합니다.
    /// </summary>
    private long CalculateQuarterBetAmount(
        PlayerControl player)
    {
        long callAmount =
            GetCurrentCallAmount(player);

        long potAfterCall =
            SafeAdd(totalPot, callAmount);

        return Math.Max(
            GetBaseMinimumBet(),
            potAfterCall / 4L
        );
    }

    private long CalculateQuarterTarget(
        PlayerControl player)
    {
        return SafeAdd(
            currentHighestBet,
            CalculateQuarterBetAmount(player)
        );
    }

    /// <summary>
    /// 한게임식 하프 표시 금액입니다.
    /// 현재 팟에 내 콜 금액을 먼저 더한 뒤 1/2을 계산합니다.
    /// </summary>
    private long CalculateHalfBetAmount(
        PlayerControl player)
    {
        long callAmount =
            GetCurrentCallAmount(player);

        long potAfterCall =
            SafeAdd(totalPot, callAmount);

        return Math.Max(
            GetBaseMinimumBet(),
            potAfterCall / 2L
        );
    }

    private long CalculateHalfTarget(
        PlayerControl player)
    {
        return SafeAdd(
            currentHighestBet,
            CalculateHalfBetAmount(player)
        );
    }

    /// <summary>
    /// 이번 베팅 라운드에서 MAX 버튼이 도달할 목표액입니다.
    /// 실제 제한은 RoundBetMoney가 아니라 블라인드를 포함한 TotalBetThisGame을 기준으로 계산합니다.
    /// </summary>
    private long CalculateMaxTarget(PlayerControl player)
    {
        return CalculateMaximumRoundTargetByGameLimit(player);
    }

    /// <summary>
    /// 이번 판 전체 맥스 한도에서 플레이어가 앞으로 더 낼 수 있는 금액입니다.
    /// 한도가 0이면 현재 보유금액 전부를 반환합니다.
    /// </summary>
    private long GetRemainingBetLimitThisGame(
        PlayerControl player)
    {
        if (player == null)
        {
            return 0L;
        }

        if (maxBetAmountPerGame <= 0L)
        {
            return Math.Max(0L, player.CurrentMoney);
        }

        return Math.Max(
            0L,
            maxBetAmountPerGame -
            player.TotalBetThisGame
        );
    }

    /// <summary>
    /// 현재 라운드 목표액 형식으로 변환한 판당 최대 베팅 목표입니다.
    /// 예: 전 라운드까지 150만 원을 냈고 판당 맥스가 400만 원이면
    /// 마지막 베팅 라운드에서 추가로 최대 250만 원까지만 낼 수 있습니다.
    /// </summary>
    private long CalculateMaximumRoundTargetByGameLimit(
        PlayerControl player)
    {
        if (player == null)
        {
            return 0L;
        }

        long additionalAmount = Math.Min(
            Math.Max(0L, player.CurrentMoney),
            GetRemainingBetLimitThisGame(player)
        );

        return SafeAdd(
            player.RoundBetMoney,
            additionalAmount
        );
    }

    private bool HasReachedBetLimitThisGame(
        PlayerControl player)
    {
        return player != null &&
               maxBetAmountPerGame > 0L &&
               player.TotalBetThisGame >=
               maxBetAmountPerGame;
    }

    private long GetPayableCallAmount(
        PlayerControl player)
    {
        if (player == null)
        {
            return 0L;
        }

        return Math.Min(
            GetCurrentCallAmount(player),
            Math.Min(
                Math.Max(0L, player.CurrentMoney),
                GetRemainingBetLimitThisGame(player)
            )
        );
    }

    private long SafeAdd(long a, long b)
    {
        if (b > 0L && a > long.MaxValue - b)
        {
            return long.MaxValue;
        }

        if (b < 0L && a < long.MinValue - b)
        {
            return long.MinValue;
        }

        return a + b;
    }

    private long SafeMultiply(long value, long multiplier)
    {
        if (value <= 0L || multiplier <= 0L)
        {
            return 0L;
        }

        if (value > long.MaxValue / multiplier)
        {
            return long.MaxValue;
        }

        return value * multiplier;
    }

    /// <summary>
    /// 사용자가 금액을 직접 올리는 자발적 올인과 맥스가 허용되는 단계인지 반환합니다.
    /// 카드 교환 전 첫 번째 베팅에서는 둘 다 금지하고,
    /// 카드 교환이 끝난 마지막 베팅에서만 허용합니다.
    /// 단, 첫 번째 베팅에서도 Call 행동으로 처리되는 잔액 부족 숏 올인 콜은 허용됩니다.
    /// </summary>
    private bool IsVoluntaryAllInOrMaxAllowed()
    {
        return currentPhase == GamePhase.FinalBetting;
    }

    /// <summary>
    /// 올인과 맥스 중 여섯 번째 칸에 어떤 토글을 표시할지 결정합니다.
    /// 이번 판에 이미 낸 금액과 현재 보유금액을 모두 합쳐도 판당 한도 이내면 올인,
    /// 그보다 많으면 맥스를 표시합니다.
    /// </summary>
    private bool ShouldShowAllInToggle(
        PlayerControl player)
    {
        if (player == null)
        {
            return true;
        }

        if (maxBetAmountPerGame <= 0L)
        {
            return true;
        }

        long allInTotalThisGame =
            SafeAdd(
                player.TotalBetThisGame,
                player.CurrentMoney
            );

        return allInTotalThisGame <=
               maxBetAmountPerGame;
    }

    /// <summary>
    /// 콜이 아닌 레이즈 계열 행동이 실제로 가능한지 판정하고
    /// 최종 목표 베팅액을 반환합니다.
    ///
    /// 따당, 쿼터, 하프는 계산된 금액을 전부 낼 수 있어야 하며,
    /// 보유 금액 또는 이번 판 맥스에 걸려 금액이 줄어드는 경우에는
    /// 해당 토글을 비활성화하고 올인/맥스 토글을 사용하게 합니다.
    /// </summary>
    private bool TryResolveLegalRaiseTarget(
        PlayerControl player,
        long requestedTargetBet,
        BettingAction action,
        out long resolvedTargetBet)
    {
        resolvedTargetBet = 0L;

        if (player == null ||
            player.CurrentMoney <= 0L)
        {
            return false;
        }

        long allInTarget =
            SafeAdd(
                player.RoundBetMoney,
                player.CurrentMoney
            );

        if (action == BettingAction.AllIn)
        {
            if (!ShouldShowAllInToggle(player))
            {
                return false;
            }

            resolvedTargetBet = allInTarget;
            return resolvedTargetBet >
                   player.RoundBetMoney;
        }

        if (action == BettingAction.Max &&
            ShouldShowAllInToggle(player))
        {
            return false;
        }

        long maximumAllowedTarget =
            CalculateMaximumRoundTargetByGameLimit(player);

        // 한게임의 쿼터/하프는 베팅 단위로 반올림하지 않습니다.
        // 계산된 정확한 목표액을 전부 낼 수 있을 때만 토글을 활성화합니다.
        if (requestedTargetBet >
            maximumAllowedTarget)
        {
            return false;
        }

        if (requestedTargetBet <=
            currentHighestBet ||
            requestedTargetBet <=
            player.RoundBetMoney)
        {
            return false;
        }

        resolvedTargetBet =
            requestedTargetBet;

        return true;
    }

    private long GetMinimumRaiseTarget()
    {
        return currentHighestBet + GetMinimumRaiseIncrement();
    }

    private long GetMinimumRaiseIncrement()
    {
        return Math.Max(
            lastRaiseIncrement,
            GetBaseMinimumBet()
        );
    }

    private long GetBaseMinimumBet()
    {
        return Math.Max(
            1L,
            Math.Max(bigBlindAmount, bettingUnit)
        );
    }

    private long RoundUpToBetUnit(long amount)
    {
        if (bettingUnit <= 1L)
        {
            return amount;
        }

        long remainder = amount % bettingUnit;

        if (remainder == 0L)
        {
            return amount;
        }

        return amount + bettingUnit - remainder;
    }

    private long RoundDownToBetUnit(long amount)
    {
        if (bettingUnit <= 1L)
        {
            return amount;
        }

        return amount - amount % bettingUnit;
    }

    private string GetBettingActionName(BettingAction action)
    {
        switch (action)
        {
            case BettingAction.Fold:
                return "다이";
            case BettingAction.Ping:
                return "삥";
            case BettingAction.Double:
                return "따당";
            case BettingAction.Call:
                return "콜";
            case BettingAction.Check:
                return "체크";
            case BettingAction.Quarter:
                return "쿼터";
            case BettingAction.Half:
                return "하프";
            case BettingAction.AllIn:
                return "올인";
            case BettingAction.Max:
                return "맥스";
            default:
                return action.ToString();
        }
    }

    #endregion

    #region Exchange Phase

    private void StartExchangePhaseImmediate()
    {
        ClearHumanBettingReservation(true);
        ClearHumanExchangeReservation(true);
        ClearAllBettingActionIcons();

        currentPhase = GamePhase.Exchange;
        currentHighestBet = 0L;

        for (int i = 0; i < players.Count; i++)
        {
            if (IsPlayerSeated(players[i]) &&
                !players[i].IsFolded)
            {
                players[i].PrepareForExchangePhase();
            }
        }

        // 교환은 딜러 왼쪽부터 시작합니다.
        int firstIndex = FindNextExchangePlayer(dealerIndex);

        if (firstIndex < 0)
        {
            StartFinalBettingPhase();
            return;
        }

        SetCurrentTurn(firstIndex);
    }

    public void SubmitExchange(
        PlayerControl player,
        List<int> selectedIndexes)
    {
        if (isHomeScreenOpen ||
            !isSessionActive ||
            !IsPlayerSeated(player))
        {
            return;
        }

        if (currentPhase != GamePhase.Exchange)
        {
            Debug.LogWarning("현재는 카드 교환 페이즈가 아닙니다.");
            return;
        }

        if (isExchangeAnimating)
        {
            Debug.LogWarning("현재 카드 교환 연출이 진행 중입니다.");
            return;
        }

        if (CurrentTurnPlayer != player)
        {
            Debug.LogWarning("현재 교환 차례인 플레이어가 아닙니다.");
            return;
        }

        if (player == null || player.IsFolded)
        {
            return;
        }

        List<int> validIndexes =
            CreateValidExchangeIndexes(selectedIndexes);

        exchangeCoroutine =
            StartCoroutine(
                ExchangeCardsRoutine(
                    player,
                    validIndexes
                )
            );
    }

    private IEnumerator ExchangeCardsRoutine(
        PlayerControl player,
        List<int> validIndexes)
    {
        isExchangeAnimating = true;
        exchangingPlayerNumber =
            player != null ? player.playerNumber : -1;

        // 0장 교환도 한 프레임 뒤에 턴을 넘겨
        // StartCoroutine 참조와 상태값이 안정적으로 정리되게 합니다.
        if (validIndexes.Count == 0)
        {
            yield return null;
        }

        for (int i = 0;
             i < validIndexes.Count;
             i++)
        {
            int handIndex = validIndexes[i];

            if (audioManager != null)
            {
                audioManager.PlayCardExchangeOne();
            }

            int oldCard =
                player.RemoveCardAtForExchange(
                    handIndex
                );

            if (CardUtility.IsValidCardNumber(oldCard) &&
                !discardedCards.Contains(oldCard))
            {
                discardedCards.Add(oldCard);
                RefreshTableCardCountUI();
            }

            // 기존 카드가 버린 카드 더미로 먼저 이동합니다.
            if (exchangeDiscardDelay > 0f)
            {
                yield return new WaitForSeconds(
                    exchangeDiscardDelay
                );
            }
            else
            {
                yield return null;
            }

            int newCard = DrawCard();

            if (newCard >= 0)
            {
                player.ReplaceCardAt(
                    handIndex,
                    newCard
                );
            }

            // 새 카드가 손패 슬롯으로 이동한 뒤 다음 카드를 처리합니다.
            if (exchangeDealCardDelay > 0f)
            {
                yield return new WaitForSeconds(
                    exchangeDealCardDelay
                );
            }
            else
            {
                yield return null;
            }
        }

        // 해당 플레이어가 교환 카드를 모두 받은 직후 손패를 다시 정렬합니다.
        // 교환 선택 인덱스 처리가 끝난 다음 정렬하므로 선택 카드가 바뀌지 않습니다.
        player.SortCardsByRankThenSuit();

        player.CompleteExchange(
            validIndexes.Count
        );

        if (aiHistoryTracker != null)
        {
            aiHistoryTracker.RecordExchange(
                player,
                validIndexes.Count,
                gameNumber
            );
        }

        Debug.Log(
            "Player " + player.playerNumber +
            " / " +
            (validIndexes.Count == 0
                ? "교환 안함"
                : "교환 " + validIndexes.Count + "장")
        );

        isExchangeAnimating = false;
        exchangingPlayerNumber = -1;
        exchangeCoroutine = null;

        // 교환이 끝난 뒤에는 더 이상 교환할 수 없으므로
        // 새 손패에 추천 선택을 다시 적용하지 않고 카드 선택도 잠급니다.
        AdvanceExchangeTurn();
    }

    private List<int> CreateValidExchangeIndexes(
        List<int> indexes)
    {
        List<int> result = new List<int>();

        if (indexes == null)
        {
            return result;
        }

        for (int i = 0; i < indexes.Count; i++)
        {
            int index = indexes[i];

            if (index < 0 || index >= 5)
            {
                continue;
            }

            if (result.Contains(index))
            {
                continue;
            }

            result.Add(index);

            if (result.Count >= maxExchangeCards)
            {
                break;
            }
        }

        result.Sort();
        return result;
    }

    private void AdvanceExchangeTurn()
    {
        if (HaveAllActivePlayersExchanged())
        {
            StartFinalBettingPhase();
            return;
        }

        int nextIndex = FindNextExchangePlayer(currentTurnIndex);

        if (nextIndex < 0)
        {
            StartFinalBettingPhase();
            return;
        }

        SetCurrentTurn(nextIndex);
    }

    private bool HaveAllActivePlayersExchanged()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player) ||
                player.IsFolded)
            {
                continue;
            }

            if (!player.HasExchangedThisGame)
            {
                return false;
            }
        }

        return true;
    }

    #endregion

    #region Hand Rank Display

    /// <summary>
    /// 최초 분배가 끝난 뒤 사람 플레이어의 족보만 공개합니다.
    /// AI 플레이어의 족보박스는 쇼다운 전까지 숨겨진 상태를 유지합니다.
    /// </summary>
    private void RefreshHumanPlayerHandRank()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player) ||
                !player.IsHumanPlayer)
            {
                continue;
            }

            if (player.IsFolded)
            {
                player.ShowFoldedHandRank();
            }
            else
            {
                player.ShowCurrentHandRank();
            }
        }
    }

    /// <summary>
    /// 게임 종료 시 모든 플레이어의 족보 또는 다이 상태를 공개합니다.
    /// </summary>
    private void RevealAllPlayerHandRanks()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player))
            {
                continue;
            }

            player.ShowHandRankAtGameEnd();
        }
    }

    #endregion

    #region Showdown And Payout

    private void StartShowdownPhaseImmediate()
    {
        ClearHumanBettingReservation(true);
        ClearHumanExchangeReservation(true);
        ClearAllBettingActionIcons();
        ClearAllWinnerObjects();

        currentPhase = GamePhase.Showdown;
        isGameFinished = false;
        isWaitingForWinnerDisplay = true;

        SetCurrentTurn(-1);

        // 쇼다운 진입 시 먼저 모든 손패와 족보를 공개하고,
        // 설정한 시간만큼 기다린 뒤 실제 승자 표시와 정산을 실행합니다.
        RevealAllPlayerHandRanks();
        RefreshGameUI();

        if (showdownResultCoroutine != null)
        {
            StopCoroutine(showdownResultCoroutine);
        }

        showdownResultCoroutine = StartCoroutine(
            ShowdownResultRoutine()
        );
    }

    private IEnumerator ShowdownResultRoutine()
    {
        if (gameEndToWinnerDisplayDelay > 0f)
        {
            yield return new WaitForSeconds(
                gameEndToWinnerDisplayDelay
            );
        }
        else
        {
            yield return null;
        }

        showdownResultCoroutine = null;
        isWaitingForWinnerDisplay = false;

        ResolveShowdownPayouts();
        ShowAllFinalGameResults();

        if (aiHistoryTracker != null)
        {
            aiHistoryTracker.CompleteHand(
                players,
                gameNumber,
                true
            );
        }

        if (audioManager != null)
        {
            audioManager.PlayWinner();
        }

        isGameFinished = true;
        RefreshGameUI();
        BeginWinnerPresentation();
    }

    /// <summary>
    /// 족보를 판정하고 메인팟과 사이드팟을 분배합니다.
    ///
    /// 한 명만 참여한 최상위 금액 구간은 실제 팟이 아니라
    /// 상대가 맞추지 않은 미콜 금액이므로 해당 플레이어에게 돌려줍니다.
    /// 미콜 금액을 돌려받은 것만으로는 위너로 표시하지 않습니다.
    /// </summary>
    private void ResolveShowdownPayouts()
    {
        // 실제 지급으로 totalPot이 0이 되기 전에 이번 판의 총 팟을 UI 표시용으로 보관합니다.
        completedHandPot = totalPot;

        List<PlayerControl> activePlayers =
            GetActivePlayersWithFiveCards();

        if (activePlayers.Count == 0)
        {
            Debug.LogError("쇼다운 가능한 플레이어가 없습니다.");
            totalPot = 0L;
            return;
        }

        List<long> contributionLevels = GetContributionLevels();

        long previousLevel = 0L;
        long distributedAmount = 0L;

        for (int levelIndex = 0;
             levelIndex < contributionLevels.Count;
             levelIndex++)
        {
            long currentLevel = contributionLevels[levelIndex];

            List<PlayerControl> contributors =
                GetPlayersAtOrAboveContributionLevel(currentLevel);

            int contributorCount = contributors.Count;

            long sidePotAmount =
                (currentLevel - previousLevel) * contributorCount;

            previousLevel = currentLevel;

            if (sidePotAmount <= 0L ||
                contributorCount <= 0)
            {
                continue;
            }

            // 이 금액 구간에 돈을 낸 플레이어가 한 명뿐이면
            // 다른 플레이어가 콜하지 않은 초과 베팅이므로 그대로 반환합니다.
            // 반환 금액은 승리금이 아니기 때문에 SetWinner(true)를 호출하지 않습니다.
            if (contributorCount == 1)
            {
                ReturnUncalledContribution(
                    contributors[0],
                    sidePotAmount
                );

                distributedAmount += sidePotAmount;
                continue;
            }

            List<PlayerControl> eligiblePlayers =
                new List<PlayerControl>();

            for (int playerIndex = 0;
                 playerIndex < activePlayers.Count;
                 playerIndex++)
            {
                PlayerControl player = activePlayers[playerIndex];

                if (player.TotalBetThisGame >= currentLevel)
                {
                    eligiblePlayers.Add(player);
                }
            }

            if (eligiblePlayers.Count == 0)
            {
                Debug.LogWarning(
                    "팟 금액 " +
                    PlayerControl.FormatKoreanMoney(sidePotAmount) +
                    "을 받을 수 있는 생존 플레이어가 없습니다. " +
                    "남은 금액 정산 단계에서 처리합니다."
                );

                continue;
            }

            List<PlayerControl> winners =
                FindBestHandPlayers(eligiblePlayers);

            DistributePotAmount(sidePotAmount, winners);
            distributedAmount += sidePotAmount;
        }

        // 비정상적인 기여금 상태 등으로 계산되지 않은 금액이 남으면
        // 전체 생존자 중 최고 족보에게 지급해 팟 금액이 사라지지 않게 합니다.
        long remainingAmount = totalPot - distributedAmount;

        if (remainingAmount > 0L)
        {
            List<PlayerControl> winners =
                FindBestHandPlayers(activePlayers);

            DistributePotAmount(remainingAmount, winners);
            distributedAmount += remainingAmount;
        }

        for (int i = 0; i < activePlayers.Count; i++)
        {
            PokerHandValue handValue =
                PokerHandEvaluator.Evaluate(activePlayers[i].cardNumbers);

            Debug.Log(
                "Player " + activePlayers[i].playerNumber +
                " / " +
                PokerHandEvaluator.GetCategoryName(handValue.Category) +
                " / 판 전체 베팅 " +
                PlayerControl.FormatKoreanMoney(
                    activePlayers[i].TotalBetThisGame
                ) +
                " / 최종 위너 " +
                activePlayers[i].IsWinnerThisGame
            );
        }

        totalPot = 0L;
    }

    private List<long> GetContributionLevels()
    {
        List<long> levels = new List<long>();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player))
            {
                continue;
            }

            long contribution = player.TotalBetThisGame;

            if (contribution <= 0L)
            {
                continue;
            }

            if (!levels.Contains(contribution))
            {
                levels.Add(contribution);
            }
        }

        levels.Sort();
        return levels;
    }

    /// <summary>
    /// 지정 기여금 이상을 이번 판에 실제로 낸 플레이어를 반환합니다.
    /// 다이 여부와 관계없이 팟 크기 계산에는 모든 기여자가 포함됩니다.
    /// </summary>
    private List<PlayerControl> GetPlayersAtOrAboveContributionLevel(
        long contributionLevel)
    {
        List<PlayerControl> result =
            new List<PlayerControl>();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player))
            {
                continue;
            }

            if (player.TotalBetThisGame >= contributionLevel)
            {
                result.Add(player);
            }
        }

        return result;
    }

    /// <summary>
    /// 다른 플레이어가 맞추지 않은 초과 베팅 금액을 원래 플레이어에게 돌려줍니다.
    /// 실제 팟 승리가 아니므로 위너 상태는 변경하지 않습니다.
    /// </summary>
    private void ReturnUncalledContribution(
        PlayerControl player,
        long amount)
    {
        if (player == null || amount <= 0L)
        {
            return;
        }

        player.AddMoney(amount);

        Debug.Log(
            "Player " + player.playerNumber +
            " 미콜 금액 반환 / " +
            PlayerControl.FormatKoreanMoney(amount)
        );
    }

    private List<PlayerControl> GetActivePlayersWithFiveCards()
    {
        List<PlayerControl> result = new List<PlayerControl>();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player) ||
                player.IsFolded)
            {
                continue;
            }

            if (player.cardNumbers == null || player.cardNumbers.Count != 5)
            {
                Debug.LogWarning(
                    "Player " + player.playerNumber +
                    "의 카드 수가 5장이 아니어서 쇼다운에서 제외됩니다."
                );
                continue;
            }

            result.Add(player);
        }

        return result;
    }

    private List<PlayerControl> FindBestHandPlayers(
        List<PlayerControl> candidates)
    {
        List<PlayerControl> winners = new List<PlayerControl>();
        PokerHandValue bestValue = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            PlayerControl player = candidates[i];
            PokerHandValue value =
                PokerHandEvaluator.Evaluate(player.cardNumbers);

            if (bestValue == null)
            {
                bestValue = value;
                winners.Add(player);
                continue;
            }

            int compare = value.CompareTo(bestValue);

            if (compare > 0)
            {
                bestValue = value;
                winners.Clear();
                winners.Add(player);
            }
            else if (compare == 0)
            {
                winners.Add(player);
            }
        }

        winners.Sort(delegate (PlayerControl a, PlayerControl b)
        {
            return a.playerNumber.CompareTo(b.playerNumber);
        });

        return winners;
    }

    private void DistributePotAmount(
        long amount,
        List<PlayerControl> winners)
    {
        if (amount <= 0L || winners == null || winners.Count == 0)
        {
            return;
        }

        long share = amount / winners.Count;
        long remainder = amount % winners.Count;

        for (int i = 0; i < winners.Count; i++)
        {
            long reward = share;

            // 나누어 떨어지지 않는 잔액은 첫 번째 승자에게 지급합니다.
            if (i == 0)
            {
                reward += remainder;
            }

            winners[i].AddMoney(reward);
            winners[i].SetWinner(true);

            PokerHandValue handValue =
                PokerHandEvaluator.Evaluate(winners[i].cardNumbers);

            Debug.Log(
                "Player " + winners[i].playerNumber +
                " 승리 / " +
                PokerHandEvaluator.GetCategoryName(handValue.Category) +
                " / 획득 " +
                PlayerControl.FormatKoreanMoney(reward)
            );
        }
    }

    /// <summary>
    /// 이전 결과의 위너/루즈 박스와 금액 텍스트를 모든 플레이어에게서 제거합니다.
    /// 새 쇼다운 또는 단독 승리 판정을 시작하기 전에 호출합니다.
    /// </summary>
    private void ClearAllWinnerObjects()
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null)
            {
                players[i].ResetGameResultUI();
            }
        }
    }

    /// <summary>
    /// 모든 팟 정산이 끝난 뒤 각 플레이어의 위너/루즈 박스와
    /// 이번 판 시작금액 대비 순이익/순손실 금액을 표시합니다.
    /// </summary>
    private void ShowAllFinalGameResults()
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (IsPlayerSeated(players[i]))
            {
                players[i].ShowFinalGameResult();
            }
        }
    }

    private void FinishWithSinglePlayer()
    {
        if (isWaitingForWinnerDisplay || isGameFinished)
        {
            return;
        }

        ClearAllBettingActionIcons();
        ClearAllWinnerObjects();

        PlayerControl winner = null;

        for (int i = 0; i < players.Count; i++)
        {
            if (IsPlayerSeated(players[i]) &&
                !players[i].IsFolded)
            {
                winner = players[i];
                break;
            }
        }

        long reward = totalPot;
        completedHandPot = reward;

        ClearHumanBettingReservation(true);
        ClearHumanExchangeReservation(true);

        currentPhase = GamePhase.Showdown;
        isGameFinished = false;
        isWaitingForWinnerDisplay = true;

        SetCurrentTurn(-1);

        // 한 명만 남아 종료된 경우에도 바로 위너 박스를 띄우지 않고,
        // 쇼다운 화면을 잠시 보여준 다음 승자를 표시합니다.
        RevealAllPlayerHandRanks();
        RefreshGameUI();

        if (showdownResultCoroutine != null)
        {
            StopCoroutine(showdownResultCoroutine);
        }

        showdownResultCoroutine = StartCoroutine(
            SinglePlayerResultRoutine(
                winner,
                reward
            )
        );
    }

    private IEnumerator SinglePlayerResultRoutine(
        PlayerControl winner,
        long reward)
    {
        if (gameEndToWinnerDisplayDelay > 0f)
        {
            yield return new WaitForSeconds(
                gameEndToWinnerDisplayDelay
            );
        }
        else
        {
            yield return null;
        }

        showdownResultCoroutine = null;
        isWaitingForWinnerDisplay = false;

        if (winner != null)
        {
            winner.AddMoney(reward);
            winner.SetWinner(true);

            Debug.Log(
                "Player " + winner.playerNumber +
                " 단독 승리 / 획득 " +
                PlayerControl.FormatKoreanMoney(reward)
            );
        }

        ShowAllFinalGameResults();

        if (aiHistoryTracker != null)
        {
            aiHistoryTracker.CompleteHand(
                players,
                gameNumber,
                false
            );
        }

        totalPot = 0L;

        if (audioManager != null)
        {
            audioManager.PlayWinner();
        }

        isGameFinished = true;
        RefreshGameUI();
        BeginWinnerPresentation();
    }

    /// <summary>
    /// 위너 표시 후 잠시 기다렸다가 테이블 칩을 승자 위치로 회수합니다.
    /// 자동 다음 게임은 칩 회수 연출이 끝난 뒤부터 지연 시간을 계산합니다.
    /// </summary>
    private void BeginWinnerPresentation()
    {
        if (winnerPresentationCoroutine != null)
        {
            StopCoroutine(winnerPresentationCoroutine);
            winnerPresentationCoroutine = null;
        }

        if (!collectTableChipsToWinners ||
            chipBetAnimator == null ||
            !chipBetAnimator.HasActiveTableChips)
        {
            StartHandCompletionAfterResultDelay();
            return;
        }

        winnerPresentationCoroutine = StartCoroutine(
            WinnerPresentationRoutine()
        );
    }

    private IEnumerator WinnerPresentationRoutine()
    {
        if (winnerDisplayBeforeChipCollectDelay > 0f)
        {
            yield return new WaitForSeconds(
                winnerDisplayBeforeChipCollectDelay
            );
        }

        List<PlayerControl> winners = GetWinnerPlayers();

        if (winners.Count > 0 &&
            chipBetAnimator != null &&
            chipBetAnimator.HasActiveTableChips)
        {
            if (audioManager != null)
            {
                audioManager.PlayChipCollect();
            }

            yield return StartCoroutine(
                chipBetAnimator.CollectTableChipsToWinnersRoutine(
                    winners
                )
            );
        }

        winnerPresentationCoroutine = null;
        StartHandCompletionAfterResultDelay();
    }

    private void StartHandCompletionAfterResultDelay()
    {
        if (handCompletionCoroutine != null)
        {
            StopCoroutine(handCompletionCoroutine);
        }

        handCompletionCoroutine =
            StartCoroutine(HandCompletionAfterResultDelayRoutine());
    }

    private IEnumerator HandCompletionAfterResultDelayRoutine()
    {
        if (bustedPlayerExitDelay > 0f)
        {
            yield return new WaitForSeconds(
                bustedPlayerExitDelay
            );
        }

        handCompletionCoroutine = null;

        if (!ProcessBustedPlayersAfterHand())
        {
            yield break;
        }

        ScheduleNextGame();
    }

    /// <summary>
    /// 정산까지 끝난 뒤 보유 금액이 0인 플레이어를 자리에서 내보냅니다.
    /// PlayerControl 오브젝트와 Player Number에 대응하는 마스크를 함께 끕니다.
    /// 사용자가 파산하거나 상대가 한 명 이하로 남으면 홈 화면으로 이동합니다.
    /// </summary>
    private bool ProcessBustedPlayersAfterHand()
    {
        bool humanBusted = false;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (!IsPlayerSeated(player) ||
                player.CurrentMoney > 0L)
            {
                continue;
            }

            bool isHuman = player.IsHumanPlayer ||
                           player.playerNumber == 0;

            RemovePlayerFromSeat(player);

            if (isHuman)
            {
                humanBusted = true;
            }
        }

        if (humanBusted)
        {
            Debug.Log("사용자 보유 금액이 0원이 되어 홈 화면으로 이동합니다.");

            // 홈 화면 전환을 먼저 완료한 다음 알람을 다시 켭니다.
            // Error Alarm Object에 붙인 EffectManager가 OnEnable에서
            // 일정 시간 후 스스로 꺼지므로 별도 타이머는 사용하지 않습니다.
            ShowHomeScreen();
            ShowErrorAlarm(humanBustedHomeMessage);

            return false;
        }

        if (GetSeatedPlayerCount() < 2)
        {
            Debug.Log("게임을 계속할 상대가 부족하여 홈 화면으로 이동합니다.");
            ShowHomeScreen();
            return false;
        }

        return !isHomeScreenOpen && isSessionActive;
    }

    private void RemovePlayerFromSeat(PlayerControl player)
    {
        if (player == null)
        {
            return;
        }

        // Player 오브젝트 밖에 배치된 족보/결과/올인 UI도 비활성화한 뒤 자리를 제거합니다.
        player.PrepareForSeatExit();
        SetPlayerMaskActive(player.playerNumber, false);

        if (player.gameObject.activeSelf)
        {
            player.gameObject.SetActive(false);
        }

        Debug.Log(
            "Player " + player.playerNumber +
            " 파산 / 자리에서 나감"
        );
    }

    private void SetPlayerMaskActive(
        int playerNumber,
        bool active)
    {
        if (playerMasks == null ||
            playerNumber < 0 ||
            playerNumber >= playerMasks.Length)
        {
            return;
        }

        GameObject maskObject =
            playerMasks[playerNumber];

        if (maskObject != null &&
            maskObject.activeSelf != active)
        {
            maskObject.SetActive(active);
        }
    }

    private List<PlayerControl> GetWinnerPlayers()
    {
        List<PlayerControl> winners = new List<PlayerControl>();

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (IsPlayerSeated(player) &&
                player.IsWinnerThisGame)
            {
                winners.Add(player);
            }
        }

        winners.Sort(delegate (PlayerControl a, PlayerControl b)
        {
            return a.playerNumber.CompareTo(b.playerNumber);
        });

        return winners;
    }

    #endregion

    #region Turn And Temporary AI

    private void SetCurrentTurn(int playerIndex)
    {
        if (isHomeScreenOpen ||
            playerIndex < 0 ||
            playerIndex >= players.Count ||
            !IsPlayerSeated(players[playerIndex]))
        {
            currentTurnIndex = -1;
        }
        else
        {
            currentTurnIndex = playerIndex;
        }

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null)
            {
                players[i].SetTurn(
                    IsPlayerSeated(players[i]) &&
                    i == currentTurnIndex
                );
            }
        }

        // 턴이 바뀔 때마다 예약 액션이 여전히 유효한지 먼저 갱신합니다.
        RefreshGameUI();

        if (CurrentTurnPlayer != null &&
            CurrentTurnPlayer.IsHumanPlayer)
        {
            if ((currentPhase == GamePhase.FirstBetting ||
                 currentPhase == GamePhase.FinalBetting) &&
                TryScheduleReservedHumanBettingAction())
            {
                return;
            }

            if (currentPhase == GamePhase.Exchange &&
                TryExecuteReservedHumanExchangeAction())
            {
                return;
            }
        }

        if (CurrentTurnPlayer != null &&
            CurrentTurnPlayer.IsComputerPlayer)
        {
            FiveCardDrawAI advancedAI =
                CurrentTurnPlayer.GetComponent<FiveCardDrawAI>();

            if (useAdvancedComputerAI &&
                advancedAI != null &&
                advancedAI.enabled &&
                advancedAI.enableAI)
            {
                advancedAI.BeginTurn(
                    this,
                    CurrentTurnPlayer,
                    currentPhase
                );
                return;
            }

            if (useTemporaryComputerAI)
            {
                StartCoroutine(
                    RunTemporaryComputerTurn(
                        CurrentTurnPlayer,
                        currentPhase
                    )
                );
            }
        }
    }

    private IEnumerator RunTemporaryComputerTurn(
        PlayerControl computer,
        GamePhase expectedPhase)
    {
        yield return new WaitForSeconds(computerActionDelay);

        if (CurrentTurnPlayer != computer)
        {
            yield break;
        }

        if (currentPhase != expectedPhase)
        {
            yield break;
        }

        if (currentPhase == GamePhase.FirstBetting ||
            currentPhase == GamePhase.FinalBetting)
        {
            if (GetCurrentCallAmount(computer) > 0L)
            {
                SubmitBettingAction(computer, BettingAction.Call);
            }
            else
            {
                SubmitBettingAction(computer, BettingAction.Check);
            }
        }
        else if (currentPhase == GamePhase.Exchange)
        {
            int exchangeCount = UnityEngine.Random.Range(
                0,
                maxExchangeCards + 1
            );

            List<int> indexes =
                CreateRandomExchangeIndexes(exchangeCount);

            SubmitExchange(computer, indexes);
        }
    }

    private List<int> CreateRandomExchangeIndexes(int count)
    {
        List<int> indexes = new List<int>
        {
            0, 1, 2, 3, 4
        };

        for (int i = indexes.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);

            int temp = indexes[i];
            indexes[i] = indexes[randomIndex];
            indexes[randomIndex] = temp;
        }

        count = Mathf.Clamp(count, 0, maxExchangeCards);
        return indexes.GetRange(0, count);
    }

    /// <summary>
    /// AI가 현재 사용할 수 있는 행동과 실제 추가 납부액을 반환합니다.
    /// UI와 같은 IsBettingActionAvailable 판정을 사용하므로 규칙이 어긋나지 않습니다.
    /// </summary>
    public List<PokerAIBettingOption> GetAvailableAIBettingOptions(
        PlayerControl player)
    {
        List<PokerAIBettingOption> result =
            new List<PokerAIBettingOption>();

        if (player == null || CurrentTurnPlayer != player)
        {
            return result;
        }

        BettingAction[] actions =
            (BettingAction[])Enum.GetValues(
                typeof(BettingAction)
            );

        for (int i = 0; i < actions.Length; i++)
        {
            BettingAction action = actions[i];

            if (!IsBettingActionAvailable(player, action))
            {
                continue;
            }

            long additionalAmount =
                GetBettingActionAdditionalAmount(
                    player,
                    action
                );

            long targetRoundBet = SafeAdd(
                player.RoundBetMoney,
                additionalAmount
            );

            bool isActualRaise =
                IsRaiseBettingAction(action) &&
                targetRoundBet > currentHighestBet;

            // 잔액이 콜 금액보다 적은 숏 올인은 Call 행동만으로도
            // 동일하게 전액 커밋됩니다. AI 후보에 AllIn을 중복 추가하면
            // 실제 처리는 콜인데 판단 로그는 올인으로 표시될 수 있어 제외합니다.
            if (action == BettingAction.AllIn &&
                !isActualRaise)
            {
                continue;
            }

            result.Add(
                new PokerAIBettingOption
                {
                    action = action,
                    additionalAmount = additionalAmount,
                    targetRoundBet = targetRoundBet,
                    isRaise = isActualRaise
                }
            );
        }

        return result;
    }

    /// <summary>
    /// 특정 행동을 선택했을 때 현재 보유금액에서 실제로 추가될 금액입니다.
    /// </summary>
    public long GetBettingActionAdditionalAmount(
        PlayerControl player,
        BettingAction action)
    {
        if (player == null)
        {
            return 0L;
        }

        switch (action)
        {
            case BettingAction.Fold:
            case BettingAction.Check:
                return 0L;

            case BettingAction.Call:
                return GetPayableCallAmount(player);

            case BettingAction.Ping:
                return Math.Max(
                    0L,
                    CalculatePingTarget(player) -
                    player.RoundBetMoney
                );

            case BettingAction.Double:
                return Math.Max(
                    0L,
                    CalculateDoubleTarget(player) -
                    player.RoundBetMoney
                );

            case BettingAction.Quarter:
                return Math.Max(
                    0L,
                    CalculateQuarterTarget(player) -
                    player.RoundBetMoney
                );

            case BettingAction.Half:
                return Math.Max(
                    0L,
                    CalculateHalfTarget(player) -
                    player.RoundBetMoney
                );

            case BettingAction.AllIn:
                return Math.Min(
                    Math.Max(0L, player.CurrentMoney),
                    GetRemainingBetLimitThisGame(player)
                );

            case BettingAction.Max:
                return Math.Max(
                    0L,
                    CalculateMaxTarget(player) -
                    player.RoundBetMoney
                );

            default:
                return 0L;
        }
    }

    public int GetSeatIndex(PlayerControl player)
    {
        return player != null
            ? players.IndexOf(player)
            : -1;
    }

    /// <summary>
    /// 폴드하지 않은 상대 수입니다. 올인 상대도 쇼다운 경쟁자이므로 포함합니다.
    /// </summary>
    public int GetActiveOpponentCount(
        PlayerControl player)
    {
        int count = 0;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl other = players[i];

            if (!IsPlayerSeated(other) ||
                other == player ||
                other.IsFolded)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// 현재 페이즈의 행동 순서에서 0은 초반, 1은 마지막 위치를 뜻합니다.
    /// 계산형 AI가 포지션 이점을 반영할 때 사용합니다.
    /// </summary>
    public float GetAIPositionScore(
        PlayerControl player)
    {
        if (!IsPlayerSeated(player) ||
            GetSeatedPlayerCount() <= 1)
        {
            return 0.5f;
        }

        int anchorIndex =
            currentPhase == GamePhase.FirstBetting
                ? bigBlindIndex
                : dealerIndex;

        List<PlayerControl> actionOrder =
            new List<PlayerControl>();

        for (int offset = 1;
             offset <= players.Count;
             offset++)
        {
            int index =
                (anchorIndex + offset + players.Count) %
                players.Count;

            PlayerControl candidate = players[index];

            if (!IsPlayerSeated(candidate) ||
                candidate.IsFolded)
            {
                continue;
            }

            if ((currentPhase == GamePhase.FirstBetting ||
                 currentPhase == GamePhase.FinalBetting) &&
                candidate.IsAllIn)
            {
                continue;
            }

            actionOrder.Add(candidate);
        }

        int position = actionOrder.IndexOf(player);

        if (position < 0 || actionOrder.Count <= 1)
        {
            return 0.5f;
        }

        return position /
               (float)(actionOrder.Count - 1);
    }

    private bool IsRaiseBettingAction(
        BettingAction action)
    {
        return action == BettingAction.Ping ||
               action == BettingAction.Double ||
               action == BettingAction.Quarter ||
               action == BettingAction.Half ||
               action == BettingAction.AllIn ||
               action == BettingAction.Max;
    }

    #endregion

    #region Search And State Helpers

    private int FindNextPlayerNeedingBettingAction(int fromIndex)
    {
        return FindNextPlayerIndex(
            fromIndex,
            NeedsBettingAction
        );
    }

    private int FindNextExchangePlayer(int fromIndex)
    {
        return FindNextPlayerIndex(
            fromIndex,
            delegate (PlayerControl player)
            {
                return !player.IsFolded &&
                       !player.HasExchangedThisGame;
            }
        );
    }

    private int FindNextPlayerIndex(
        int fromIndex,
        Predicate<PlayerControl> condition)
    {
        if (players.Count == 0 || condition == null)
        {
            return -1;
        }

        for (int offset = 1; offset <= players.Count; offset++)
        {
            int index =
                (fromIndex + offset + players.Count) % players.Count;

            PlayerControl player = players[index];

            if (!IsPlayerSeated(player))
            {
                continue;
            }

            if (condition(player))
            {
                return index;
            }
        }

        return -1;
    }

    private int FindNextSeatedPlayerIndex(int fromIndex)
    {
        if (players.Count == 0)
        {
            return -1;
        }

        for (int offset = 1; offset <= players.Count; offset++)
        {
            int index =
                (fromIndex + offset + players.Count) % players.Count;

            if (IsPlayerSeated(players[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private int GetNextSeatIndex(int fromIndex)
    {
        return FindNextSeatedPlayerIndex(fromIndex);
    }

    private bool IsPlayerSeated(PlayerControl player)
    {
        return player != null &&
               player.gameObject != null &&
               player.gameObject.activeSelf;
    }

    private int GetSeatedPlayerCount()
    {
        int count = 0;

        for (int i = 0; i < players.Count; i++)
        {
            if (IsPlayerSeated(players[i]))
            {
                count++;
            }
        }

        return count;
    }

    private int GetActivePlayerCount()
    {
        int count = 0;

        for (int i = 0; i < players.Count; i++)
        {
            if (IsPlayerSeated(players[i]) &&
                !players[i].IsFolded)
            {
                count++;
            }
        }

        return count;
    }

    private int GetPlayersAbleToBetCount()
    {
        int count = 0;

        for (int i = 0; i < players.Count; i++)
        {
            if (CanPlayerBet(players[i]))
            {
                count++;
            }
        }

        return count;
    }

    public long GetCurrentCallAmount(PlayerControl player)
    {
        if (player == null)
        {
            return 0L;
        }

        return Math.Max(
            0L,
            currentHighestBet - player.RoundBetMoney
        );
    }

    #endregion

    #region UI

    /// <summary>
    /// 인자로 받은 문구를 표시하고 알람 오브젝트를 false → true로 전환합니다.
    /// 같은 알람이 이미 켜져 있어도 매번 OnEnable/Animator 애니메이션을 다시 시작할 수 있습니다.
    /// </summary>
    public void ShowErrorAlarm(string message)
    {
        if (errorAlarmText == null && errorAlarmObject != null)
        {
            errorAlarmText =
                errorAlarmObject.GetComponentInChildren<Text>(true);
        }

        if (errorAlarmText != null)
        {
            errorAlarmText.text = message ?? string.Empty;
        }

        if (errorAlarmObject == null)
        {
            Debug.LogWarning(
                "FiveCardDrawGameManager: Error Alarm Object가 연결되지 않았습니다. / " +
                (message ?? string.Empty)
            );
            return;
        }

        errorAlarmObject.SetActive(false);
        errorAlarmObject.SetActive(true);
    }

    private void RefreshGameUI()
    {
        RefreshTableCardCountUI();
        RefreshRoundStateObjects();

        if (phaseText != null)
        {
            phaseText.text = GetPhaseDisplayText();
        }

        if (potText != null)
        {
            long displayedPot =
                currentPhase == GamePhase.Showdown &&
                completedHandPot > 0L
                    ? completedHandPot
                    : totalPot;

            potText.text =
                PlayerControl.FormatKoreanChipAmount(displayedPot);
        }

        if (currentCallText != null)
        {
            if (currentPhase == GamePhase.FirstBetting ||
                currentPhase == GamePhase.FinalBetting)
            {
                currentCallText.text =
                    PlayerControl.FormatKoreanChipAmount(
                        GetCurrentCallAmount(CurrentTurnPlayer)
                    );
            }
            else
            {
                currentCallText.text = string.Empty;
            }
        }

        RefreshHumanActionBoxUI();
        RefreshHumanBettingToggleUI();
        RefreshHumanExchangeToggleUI();
    }

    /// <summary>
    /// 테이블에 남아 있는 미분배 카드 수와 이번 판에 교환한 카드 수를 표시합니다.
    /// 남은카드 : 52장 / 버린카드 : 10장 형식으로 표시합니다.
    /// 홈 화면 또는 세션 중지 상태에서는 두 값을 0장으로 표시합니다.
    /// </summary>
    private void RefreshTableCardCountUI()
    {
        bool showSessionCount =
            isSessionActive &&
            !isHomeScreenOpen;

        int remainingCount = showSessionCount
            ? Mathf.Max(0, deck.Count - deckCursor)
            : 0;

        int exchangedCount = showSessionCount
            ? Mathf.Max(0, discardedCards.Count)
            : 0;

        if (remainingCardText != null)
        {
            remainingCardText.text =
                "남은카드 : " +
                remainingCount +
                "장";
        }

        if (exchangedCardText != null)
        {
            exchangedCardText.text =
                "버린카드 : " +
                exchangedCount +
                "장";
        }
    }

    /// <summary>
    /// 현재 게임 진행 단계에 맞춰 상태 오브젝트 세 개 중 하나만 켭니다.
    /// 준비, 블라인드, 카드 분배, 쇼다운, 홈 화면에서는 모두 끕니다.
    /// </summary>
    private void RefreshRoundStateObjects()
    {
        bool canShowRoundState =
            isSessionActive &&
            !isHomeScreenOpen;

        SetGameObjectActiveIfNeeded(
            firstBettingRoundObject,
            canShowRoundState &&
            currentPhase == GamePhase.FirstBetting
        );

        SetGameObjectActiveIfNeeded(
            exchangeRoundObject,
            canShowRoundState &&
            currentPhase == GamePhase.Exchange
        );

        SetGameObjectActiveIfNeeded(
            finalBettingRoundObject,
            canShowRoundState &&
            currentPhase == GamePhase.FinalBetting
        );
    }

    private void SetGameObjectActiveIfNeeded(
        GameObject target,
        bool active)
    {
        if (target != null &&
            target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    /// <summary>
    /// 교환 페이즈에서만 교환 박스를 표시하고,
    /// 그 외에는 기본 베팅 박스를 표시합니다.
    /// </summary>
    private void RefreshHumanActionBoxUI()
    {
        PlayerControl humanPlayer =
            GetPlayerByNumber(0);

        bool canShowGameActions =
            !isHomeScreenOpen &&
            isSessionActive &&
            IsPlayerSeated(humanPlayer);

        bool isExchangePhase =
            canShowGameActions &&
            currentPhase == GamePhase.Exchange;

        bool showBettingBox =
            canShowGameActions &&
            !isExchangePhase;

        if (bettingBox != null &&
            bettingBox.activeSelf != showBettingBox)
        {
            bettingBox.SetActive(showBettingBox);
        }

        if (exchangeBox != null &&
            exchangeBox.activeSelf != isExchangePhase)
        {
            exchangeBox.SetActive(isExchangePhase);
        }
    }

    /// <summary>
    /// 한게임식 고정 6칸 베팅 UI를 갱신합니다.
    ///
    /// [다이]
    /// [삥 또는 따당]
    /// [콜 또는 체크]
    /// [쿼터]
    /// [하프]
    /// [올인 또는 맥스]
    ///
    /// 항상 위 여섯 자리만 활성화하고,
    /// 현재 사용할 수 없는 행동은 Toggle.interactable을 false로 만듭니다.
    /// </summary>
    private void RefreshHumanBettingToggleUI()
    {
        PlayerControl humanPlayer =
            GetPlayerByNumber(0);

        long humanCallAmount =
            GetCurrentCallAmount(humanPlayer);

        // 한게임처럼 콜 금액이 없으면 삥, 있으면 따당을 표시합니다.
        bool showDouble =
            humanCallAmount > 0L;

        bool showCall =
            humanCallAmount > 0L;

        bool showAllIn =
            ShouldShowAllInToggle(
                humanPlayer
            );

        // 상대 턴이어도 현재 상태에서 선택 가능한 액션이면 interactable을 유지합니다.
        // 내 턴 여부는 예약 실행 시점에만 검사합니다.
        SetBettingToggleState(
            foldToggle,
            BettingAction.Fold,
            true,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Fold,
                false
            )
        );

        SetBettingToggleState(
            pingToggle,
            BettingAction.Ping,
            !showDouble,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Ping,
                false
            )
        );

        SetBettingToggleState(
            doubleToggle,
            BettingAction.Double,
            showDouble,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Double,
                false
            )
        );

        SetBettingToggleState(
            callToggle,
            BettingAction.Call,
            showCall,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Call,
                false
            )
        );

        SetBettingToggleState(
            checkToggle,
            BettingAction.Check,
            !showCall,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Check,
                false
            )
        );

        SetBettingToggleState(
            quarterToggle,
            BettingAction.Quarter,
            true,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Quarter,
                false
            )
        );

        SetBettingToggleState(
            halfToggle,
            BettingAction.Half,
            true,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Half,
                false
            )
        );

        SetBettingToggleState(
            allInToggle,
            BettingAction.AllIn,
            showAllIn,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.AllIn,
                false
            )
        );

        SetBettingToggleState(
            maxToggle,
            BettingAction.Max,
            !showAllIn,
            IsBettingActionAvailable(
                humanPlayer,
                BettingAction.Max,
                false
            )
        );

        // 상대방 턴이어도 상대 베팅으로 팟과 콜 금액이 바뀌는 즉시
        // 모든 토글의 표시 금액을 최신 값으로 갱신합니다.
        RefreshHumanBettingAmountUI(
            humanPlayer,
            showDouble,
            showCall,
            showAllIn
        );
    }

    private void SetBettingToggleState(
        Toggle targetToggle,
        BettingAction action,
        bool active,
        bool interactable)
    {
        if (targetToggle == null)
        {
            return;
        }

        bool canSelect =
            active && interactable;

        bool isReservedAction =
            reservedHumanBettingAction == (int)action;

        // 예약해 둔 액션이 표시 교체 또는 금액 변화로 불가능해진 순간
        // 예약과 체크 상태를 함께 해제합니다.
        if (isReservedAction &&
            !canSelect)
        {
            CancelReservedHumanBettingDelay();
            reservedHumanBettingAction = -1;
            isReservedAction = false;
        }

        if (targetToggle.gameObject.activeSelf != active)
        {
            targetToggle.gameObject.SetActive(active);
        }

        targetToggle.interactable =
            canSelect;

        bool shouldBeOn =
            isReservedAction && canSelect;

        if (targetToggle.isOn != shouldBeOn)
        {
            // UI 자동 갱신으로 예약 행동이 실행되지 않도록 이벤트 없이 변경합니다.
            targetToggle.SetIsOnWithoutNotify(shouldBeOn);
        }
    }


    /// <summary>
    /// 교환 박스의 패스/교환 토글과 예약 체크 상태를 갱신합니다.
    /// 패스는 선택 카드가 없어도 가능하고,
    /// 교환은 선택한 카드가 한 장 이상일 때만 interactable이 true입니다.
    /// </summary>
    private void RefreshHumanExchangeToggleUI()
    {
        PlayerControl humanPlayer =
            GetPlayerByNumber(0);

        bool passAvailable =
            IsHumanExchangeActionAvailable(
                humanPlayer,
                ExchangeAction.Pass,
                false
            );

        bool exchangeAvailable =
            IsHumanExchangeActionAvailable(
                humanPlayer,
                ExchangeAction.Exchange,
                false
            );

        SetHumanExchangeToggleState(
            passToggle,
            ExchangeAction.Pass,
            passAvailable
        );

        SetHumanExchangeToggleState(
            exchangeToggle,
            ExchangeAction.Exchange,
            exchangeAvailable
        );
    }

    private void SetHumanExchangeToggleState(
        Toggle targetToggle,
        ExchangeAction action,
        bool interactable)
    {
        if (targetToggle == null)
        {
            return;
        }

        // 패스와 교환 토글은 교환 박스 안에서 항상 함께 보입니다.
        // 실제 표시 여부는 부모 exchangeBox가 담당합니다.
        if (!targetToggle.gameObject.activeSelf)
        {
            targetToggle.gameObject.SetActive(true);
        }

        bool isReservedAction =
            reservedHumanExchangeAction == (int)action;

        if (isReservedAction &&
            !interactable)
        {
            reservedHumanExchangeAction = -1;
            isReservedAction = false;
        }

        targetToggle.interactable =
            interactable;

        bool shouldBeOn =
            isReservedAction && interactable;

        if (targetToggle.isOn != shouldBeOn)
        {
            targetToggle.SetIsOnWithoutNotify(shouldBeOn);
        }
    }

    /// <summary>
    /// 9개 토글 아래의 금액 박스와 Text를 갱신합니다.
    /// 다이와 체크는 금액이 없으므로 박스를 항상 끕니다.
    /// 숨겨진 짝 토글의 박스도 함께 꺼서 다음 표시 전환 때 잔상이 남지 않게 합니다.
    /// </summary>
    private void RefreshHumanBettingAmountUI(
        PlayerControl humanPlayer,
        bool showDouble,
        bool showCall,
        bool showAllIn)
    {
        SetBettingAmountState(
            foldAmountUI,
            false,
            0L
        );

        SetBettingAmountState(
            pingAmountUI,
            !showDouble,
            CalculatePingBetAmount()
        );

        SetBettingAmountState(
            doubleAmountUI,
            showDouble,
            CalculateDoubleBetAmount(humanPlayer)
        );

        SetBettingAmountState(
            callAmountUI,
            showCall,
            GetPayableCallAmount(humanPlayer)
        );

        SetBettingAmountState(
            checkAmountUI,
            false,
            0L
        );

        SetBettingAmountState(
            quarterAmountUI,
            true,
            CalculateQuarterBetAmount(humanPlayer)
        );

        SetBettingAmountState(
            halfAmountUI,
            true,
            CalculateHalfBetAmount(humanPlayer)
        );

        long allInAdditionalAmount =
            humanPlayer != null
                ? humanPlayer.CurrentMoney
                : 0L;

        SetBettingAmountState(
            allInAmountUI,
            showAllIn,
            allInAdditionalAmount
        );

        long maxAdditionalAmount = 0L;

        if (humanPlayer != null)
        {
            maxAdditionalAmount = Math.Max(
                0L,
                CalculateMaxTarget(humanPlayer) -
                humanPlayer.RoundBetMoney
            );
        }

        SetBettingAmountState(
            maxAmountUI,
            !showAllIn,
            maxAdditionalAmount
        );
    }

    private void SetBettingAmountState(
        BettingAmountUIReference amountUI,
        bool visible,
        long amount)
    {
        if (amountUI == null ||
            amountUI.boxObject == null)
        {
            return;
        }

        bool shouldShow =
            visible && amount > 0L;

        if (amountUI.boxObject.activeSelf != shouldShow)
        {
            amountUI.boxObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        Text amountText =
            amountUI.ResolveText();

        if (amountText != null)
        {
            amountText.text =
                PlayerControl.FormatKoreanMoney(amount);
        }
    }

    private string GetPhaseDisplayText()
    {
        switch (currentPhase)
        {
            case GamePhase.Preparing:
                return "게임 준비 중";
            case GamePhase.FirstBetting:
                return "첫 번째 베팅";
            case GamePhase.Exchange:
                return "카드 교환";
            case GamePhase.FinalBetting:
                return "마지막 베팅";
            case GamePhase.Showdown:
                return "쇼다운";
            default:
                return string.Empty;
        }
    }

    #endregion

#if UNITY_EDITOR
    private void OnValidate()
    {
        smallBlindAmount = Math.Max(0L, smallBlindAmount);
        bigBlindAmount = Math.Max(1L, bigBlindAmount);
        bettingUnit = Math.Max(1L, bettingUnit);
        pingBetAmount = Math.Max(0L, pingBetAmount);
        maxBetAmountPerGame = Math.Max(0L, maxBetAmountPerGame);
        maxExchangeCards = Mathf.Clamp(maxExchangeCards, 0, 5);
        reservedHumanBettingExecutionDelay =
            Mathf.Max(0f, reservedHumanBettingExecutionDelay);
        nextGameDelay = Mathf.Max(0f, nextGameDelay);
        computerActionDelay = Mathf.Max(0f, computerActionDelay);

        gameStartBeforeBlindDelay =
            Mathf.Max(0f, gameStartBeforeBlindDelay);
        blindPostInterval =
            Mathf.Max(0f, blindPostInterval);
        blindsToInitialDealDelay =
            Mathf.Max(0f, blindsToInitialDealDelay);
        initialDealToFirstBettingDelay =
            Mathf.Max(0f, initialDealToFirstBettingDelay);
        firstBettingToExchangeDelay =
            Mathf.Max(0f, firstBettingToExchangeDelay);
        exchangeToFinalBettingDelay =
            Mathf.Max(0f, exchangeToFinalBettingDelay);
        finalBettingToShowdownDelay =
            Mathf.Max(0f, finalBettingToShowdownDelay);
        gameEndToWinnerDisplayDelay =
            Mathf.Max(0f, gameEndToWinnerDisplayDelay);

        initialDealCardDelay = Mathf.Max(0f, initialDealCardDelay);
        exchangeDiscardDelay = Mathf.Max(0f, exchangeDiscardDelay);
        exchangeDealCardDelay = Mathf.Max(0f, exchangeDealCardDelay);
        winnerDisplayBeforeChipCollectDelay =
            Mathf.Max(0f, winnerDisplayBeforeChipCollectDelay);

        if (cardSprites == null ||
            cardSprites.Length != CardUtility.TotalCardCount)
        {
            Array.Resize(
                ref cardSprites,
                CardUtility.TotalCardCount
            );
        }
    }
#endif
}