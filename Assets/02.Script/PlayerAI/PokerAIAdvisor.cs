using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player 0에게 현재 패, 팟오즈, 상대의 누적 행동 패턴, 블러프/트랩 가능성,
/// 카드 교환 장수, 최근 흐름과 스택 압박을 종합해 100자 이내 조언을 제공합니다.
///
/// 별도의 서버나 외부 API 없이 프로젝트에 이미 존재하는
/// PokerAIBrain과 PokerAIHistoryTracker를 활용합니다.
///
/// 권장 부착 위치:
/// - FiveCardDrawGameManager와 같은 오브젝트
/// - 또는 별도의 "AI Advisor" 오브젝트
///
/// 갱신 시점:
/// - 새 판 시작
/// - 베팅/교환 페이즈 변경
/// - 상대가 베팅 행동을 완료했을 때
/// - 상대가 카드 교환을 완료했을 때
/// - Player 0의 턴이 시작될 때
/// - Player 0이 교환 카드를 선택/취소했을 때
/// - 쇼다운 진입
/// </summary>
[DisallowMultipleComponent]
public class PokerAIAdvisor : MonoBehaviour
{
    private enum AdviceTrigger
    {
        None = 0,
        NewHand = 1,
        PhaseChanged = 2,
        HumanTurn = 3,
        OpponentBettingAction = 4,
        OpponentExchange = 5,
        HumanExchangeSelection = 6,
        StateChanged = 7,
        Showdown = 8
    }

    private sealed class AdviceCandidate
    {
        public int templateId;
        public float score;
        public string text;

        public AdviceCandidate(
            int templateId,
            float score,
            string text)
        {
            this.templateId = templateId;
            this.score = score;
            this.text = text;
        }
    }

    private sealed class HandInsight
    {
        public PokerHandValue value;
        public PokerHandCategory category;
        public string categoryName;
        public string keyRankName;
        public string drawText;
        public float normalizedStrength;
        public bool hasFourFlush;
        public bool hasFourStraight;
        public int pairCount;
        public int maximumSameRankCount;
    }

    private sealed class OpponentInsight
    {
        public PlayerControl player;
        public PokerAIOpponentRead read;
        public PokerPlayerHistory history;
        public float relevanceScore;
        public bool hasReliableActionSample;
        public bool hasReliableShowdownSample;
    }

    private sealed class ExchangeConsensusRecord
    {
        public List<int> indexes = new List<int>();
        public int votes;
        public float weightedScore;
        public float expectedScoreTotal;
        public float confidenceTotal;
    }

    [Header("Required References")]
    [Tooltip("게임 전체 진행을 관리하는 FiveCardDrawGameManager입니다. 비워두면 같은 오브젝트와 부모에서 자동 탐색합니다.")]
    [SerializeField] private FiveCardDrawGameManager gameManager;

    [Tooltip("AI 조언을 표시할 Legacy Text입니다.")]
    [SerializeField] private Text adviceText;

    [Tooltip("AI 도우미 전체 UI 오브젝트입니다. 선택 사항입니다.")]
    [SerializeField] private GameObject advisorObject;

    [Header("Player Names")]
    [Tooltip("Player Number 0~4 순서입니다. 0번은 사용자 표시명입니다.")]
    [SerializeField]
    private string[] playerNames =
    {
        "나",
        "은채",
        "은우",
        "아름",
        "다운"
    };

    [Header("Advice Timing")]
    [Tooltip("게임 상태를 확인하는 간격입니다. 너무 짧게 설정할 필요는 없습니다.")]
    [Min(0.05f)]
    [SerializeField] private float stateCheckInterval = 0.12f;

    [Tooltip("상태가 바뀐 뒤 AI가 분석하는 것처럼 잠시 기다렸다가 문구를 표시합니다.")]
    [Min(0f)]
    [SerializeField] private float analysisDelay = 0.28f;

    [Tooltip("문구가 지나치게 빠르게 연속 교체되는 것을 막는 최소 간격입니다.")]
    [Min(0f)]
    [SerializeField] private float minimumAdviceInterval = 0.45f;

    [Tooltip("홈 화면에서는 AI 도우미 오브젝트를 숨깁니다.")]
    [SerializeField] private bool hideAdvisorOnHome = true;

    [Tooltip("홈 화면에서도 오브젝트를 유지할 경우 표시할 문구입니다.")]
    [TextArea(2, 3)]
    [SerializeField]
    private string homeAdvice =
        "게임을 시작하면 패와 상대 행동을 함께 분석해드릴게요.";

    [Header("Advice Length")]
    [Tooltip("요청 규칙에 따라 기본 100자로 제한합니다.")]
    [Range(40, 100)]
    [SerializeField] private int maximumCharacters = 100;

    [Header("AI Analysis Accuracy")]
    [Tooltip("베팅 승산 추정에 사용할 몬테카를로 횟수입니다. 높을수록 안정적이지만 연산량이 증가합니다.")]
    [Range(40, 600)]
    [SerializeField] private int bettingMonteCarloSamples = 220;

    [Tooltip("교환 후보 하나당 시험할 횟수입니다.")]
    [Range(60, 600)]
    [SerializeField] private int exchangeMonteCarloSamplesPerCandidate = 220;

    [Tooltip("교환 추천을 서로 다른 고정 시드로 여러 번 분석한 뒤 다수결로 합칩니다.")]
    [Range(1, 5)]
    [SerializeField] private int exchangeConsensusPasses = 3;

    [Tooltip("원페어, 투페어, 트리플, 4플러시, 4스트레이트 같은 명확한 구조는 전략 규칙으로 보호합니다.")]
    [SerializeField] private bool useStrategicExchangeGuardrails = true;

    [Tooltip("같은 판의 같은 카드 5장에서는 선택을 바꿔도 교환 추천을 고정합니다.")]
    [SerializeField] private bool keepExchangeRecommendationStable = true;

    [Header("Coach Personality")]
    [Tooltip("도우미는 계산형을 기본으로 하되 지나치게 소극적이지 않게 판단합니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float coachAggression = 0.52f;

    [Range(0f, 1f)]
    [SerializeField] private float coachHandSelectivity = 0.82f;

    [Range(0f, 1f)]
    [SerializeField] private float coachBluffTendency = 0.14f;

    [Header("Variation")]
    [Tooltip("같은 템플릿이 바로 반복되지 않도록 기억할 개수입니다.")]
    [Range(2, 12)]
    [SerializeField] private int recentTemplateMemory = 7;

    [Tooltip("완전히 같은 문장이 다시 나오는 것을 피하기 위해 기억할 문장 수입니다.")]
    [Range(2, 12)]
    [SerializeField] private int recentMessageMemory = 8;

    [Header("Debug")]
    [SerializeField] private bool logAdviceAnalysis;

    private PlayerControl humanPlayer;
    private PokerAIHistoryTracker historyTracker;
    private System.Random random;

    private float nextStateCheckTime;
    private float lastAdviceDisplayedTime = -999f;
    private string lastStateSignature = string.Empty;

    private int observedGameNumber = -1;
    private GamePhase observedPhase = GamePhase.Preparing;
    private int observedTurnIndex = -999;
    private int observedSelectionHash;
    private bool snapshotInitialized;

    private readonly int[] observedBettingActionCounts =
        new int[5];

    private readonly int[] observedExchangeDecisionCounts =
        new int[5];

    private readonly Queue<int> recentTemplateIds =
        new Queue<int>();

    private readonly Queue<string> recentMessages =
        new Queue<string>();

    private Coroutine pendingAdviceCoroutine;
    private int adviceRequestVersion;

    private string cachedBettingDecisionKey = string.Empty;
    private PokerAIBettingDecision cachedBettingDecision;

    private string cachedExchangeDecisionKey = string.Empty;
    private PokerAIExchangeDecision cachedExchangeDecision;

    private void Awake()
    {
        InitializeRandom();
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResetObservationState();
        RefreshHomeVisibility();
    }

    private void OnDisable()
    {
        CancelPendingAdvice();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextStateCheckTime)
        {
            return;
        }

        nextStateCheckTime =
            Time.unscaledTime +
            Mathf.Max(0.05f, stateCheckInterval);

        ResolveReferences();

        if (gameManager == null || adviceText == null)
        {
            return;
        }

        RefreshHomeVisibility();

        if (gameManager.IsPaused)
        {
            // 퍼즈 중에는 현재 조언을 그대로 유지합니다.
            return;
        }

        if (gameManager.IsHomeScreenOpen ||
            !gameManager.IsSessionActive)
        {
            HandleHomeState();
            return;
        }

        if (humanPlayer == null)
        {
            humanPlayer = gameManager.GetPlayerByNumber(0);
        }

        if (humanPlayer == null)
        {
            return;
        }

        string currentSignature = BuildStateSignature();

        if (!snapshotInitialized)
        {
            CaptureObservationSnapshot();
            lastStateSignature = currentSignature;
            snapshotInitialized = true;

            RequestAdvice(
                AdviceTrigger.NewHand,
                -1
            );
            return;
        }

        if (currentSignature == lastStateSignature)
        {
            return;
        }

        int changedBettingPlayer;
        int changedExchangePlayer;

        DetectChangedPlayers(
            out changedBettingPlayer,
            out changedExchangePlayer
        );

        AdviceTrigger trigger =
            DetermineTrigger(
                changedBettingPlayer,
                changedExchangePlayer
            );

        int relatedPlayerNumber =
            changedBettingPlayer >= 0
                ? changedBettingPlayer
                : changedExchangePlayer;

        CaptureObservationSnapshot();
        lastStateSignature = currentSignature;

        RequestAdvice(
            trigger,
            relatedPlayerNumber
        );
    }

    /// <summary>
    /// 외부 버튼이나 연출에서 즉시 조언을 다시 요청할 때 사용할 수 있습니다.
    /// </summary>
    public void RefreshAdviceNow()
    {
        ResolveReferences();

        if (gameManager == null ||
            adviceText == null ||
            gameManager.IsHomeScreenOpen ||
            !gameManager.IsSessionActive)
        {
            return;
        }

        CaptureObservationSnapshot();
        lastStateSignature = BuildStateSignature();

        RequestAdvice(
            gameManager.CurrentTurnPlayer == humanPlayer
                ? AdviceTrigger.HumanTurn
                : AdviceTrigger.StateChanged,
            -1,
            true
        );
    }

    /// <summary>
    /// 게임을 처음부터 다시 시작할 때 최근 문장 기억과 상태 감지를 초기화합니다.
    /// GameManager에서 직접 호출하지 않아도 GameNumber 변경을 통해 자동 처리됩니다.
    /// </summary>
    public void ResetAdvisor()
    {
        CancelPendingAdvice();

        recentTemplateIds.Clear();
        recentMessages.Clear();

        ResetObservationState();

        if (adviceText != null)
        {
            adviceText.text = string.Empty;
        }

        RefreshHomeVisibility();
    }

    private void ResolveReferences()
    {
        if (gameManager == null)
        {
            gameManager =
                GetComponent<FiveCardDrawGameManager>();

            if (gameManager == null)
            {
                gameManager =
                    GetComponentInParent<FiveCardDrawGameManager>();
            }

            if (gameManager == null)
            {
                gameManager =
                    FindFirstObjectByType<FiveCardDrawGameManager>();
            }
        }

        if (gameManager != null)
        {
            historyTracker = gameManager.AIHistoryTracker;

            if (humanPlayer == null)
            {
                humanPlayer =
                    gameManager.GetPlayerByNumber(0);
            }
        }

        EnsurePlayerNames();
    }

    private void EnsurePlayerNames()
    {
        if (playerNames != null &&
            playerNames.Length >= 5)
        {
            return;
        }

        string[] corrected =
        {
            "나",
            "은채",
            "은우",
            "아름",
            "다운"
        };

        if (playerNames != null)
        {
            int copyCount =
                Mathf.Min(
                    playerNames.Length,
                    corrected.Length
                );

            for (int i = 0; i < copyCount; i++)
            {
                if (!string.IsNullOrWhiteSpace(playerNames[i]))
                {
                    corrected[i] = playerNames[i];
                }
            }
        }

        playerNames = corrected;
    }

    private void RefreshHomeVisibility()
    {
        if (advisorObject == null ||
            gameManager == null)
        {
            return;
        }

        // 이 스크립트가 Advisor Object 자신이나 그 자식에 붙어 있을 때
        // 부모를 꺼버리면 다시 켤 수 없으므로 활성 상태는 변경하지 않습니다.
        bool scriptWouldBeDisabled =
            advisorObject == gameObject ||
            transform.IsChildOf(advisorObject.transform);

        if (scriptWouldBeDisabled)
        {
            return;
        }

        bool shouldShow =
            !hideAdvisorOnHome ||
            (!gameManager.IsHomeScreenOpen &&
             gameManager.IsSessionActive);

        if (advisorObject.activeSelf != shouldShow)
        {
            advisorObject.SetActive(shouldShow);
        }
    }

    private void HandleHomeState()
    {
        CancelPendingAdvice();

        if (!hideAdvisorOnHome &&
            adviceText != null &&
            adviceText.text != homeAdvice)
        {
            adviceText.text =
                NormalizeAdvice(homeAdvice);
        }

        ResetObservationState();
    }

    private void ResetObservationState()
    {
        ClearDecisionCaches();

        snapshotInitialized = false;
        observedGameNumber = -1;
        observedPhase = GamePhase.Preparing;
        observedTurnIndex = -999;
        observedSelectionHash = 0;
        lastStateSignature = string.Empty;

        for (int i = 0;
             i < observedBettingActionCounts.Length;
             i++)
        {
            observedBettingActionCounts[i] = 0;
            observedExchangeDecisionCounts[i] = 0;
        }
    }

    private string BuildStateSignature()
    {
        StringBuilder builder =
            new StringBuilder(256);

        builder.Append(gameManager.GameNumber)
            .Append('|')
            .Append((int)gameManager.CurrentPhase)
            .Append('|')
            .Append(gameManager.CurrentTurnIndex)
            .Append('|')
            .Append(gameManager.TotalPot)
            .Append('|')
            .Append(gameManager.CurrentHighestBet)
            .Append('|')
            .Append(gameManager.IsGameFinished ? 1 : 0);

        if (humanPlayer != null)
        {
            builder.Append('|')
                .Append(humanPlayer.CurrentMoney)
                .Append('|')
                .Append(humanPlayer.RoundBetMoney)
                .Append('|')
                .Append(humanPlayer.IsFolded ? 1 : 0)
                .Append('|')
                .Append(humanPlayer.IsAllIn ? 1 : 0)
                .Append('|')
                .Append(humanPlayer.HasExchangedThisGame ? 1 : 0)
                .Append('|')
                .Append(GetSelectionHash(humanPlayer));
        }

        for (int i = 0;
             i < gameManager.players.Count;
             i++)
        {
            PlayerControl player =
                gameManager.players[i];

            if (player == null)
            {
                builder.Append("|null");
                continue;
            }

            builder.Append('|')
                .Append(player.playerNumber)
                .Append(':')
                .Append(player.CurrentMoney)
                .Append(':')
                .Append(player.RoundBetMoney)
                .Append(':')
                .Append(player.TotalBetThisGame)
                .Append(':')
                .Append(player.IsFolded ? 1 : 0)
                .Append(':')
                .Append(player.IsAllIn ? 1 : 0)
                .Append(':')
                .Append(player.HasExchangedThisGame ? 1 : 0)
                .Append(':')
                .Append(player.ExchangedCardCount);

            if (historyTracker != null)
            {
                PokerPlayerHistory history =
                    historyTracker.GetHistory(
                        player.playerNumber
                    );

                builder.Append(':')
                    .Append(history.bettingActionCount)
                    .Append(':')
                    .Append(history.exchangeDecisionCount)
                    .Append(':')
                    .Append((int)history.lastBettingAction)
                    .Append(':')
                    .Append(history.lastBettingGameNumber);
            }
        }

        return builder.ToString();
    }

    private void CaptureObservationSnapshot()
    {
        observedGameNumber =
            gameManager != null
                ? gameManager.GameNumber
                : -1;

        observedPhase =
            gameManager != null
                ? gameManager.CurrentPhase
                : GamePhase.Preparing;

        observedTurnIndex =
            gameManager != null
                ? gameManager.CurrentTurnIndex
                : -999;

        observedSelectionHash =
            humanPlayer != null
                ? GetSelectionHash(humanPlayer)
                : 0;

        for (int playerNumber = 0;
             playerNumber < 5;
             playerNumber++)
        {
            observedBettingActionCounts[playerNumber] =
                GetBettingActionCount(playerNumber);

            observedExchangeDecisionCounts[playerNumber] =
                GetExchangeDecisionCount(playerNumber);
        }
    }

    private void DetectChangedPlayers(
        out int changedBettingPlayer,
        out int changedExchangePlayer)
    {
        changedBettingPlayer = -1;
        changedExchangePlayer = -1;

        int largestBettingDelta = 0;
        int largestExchangeDelta = 0;

        for (int playerNumber = 0;
             playerNumber < 5;
             playerNumber++)
        {
            int currentBettingCount =
                GetBettingActionCount(playerNumber);

            int bettingDelta =
                currentBettingCount -
                observedBettingActionCounts[playerNumber];

            if (bettingDelta > largestBettingDelta)
            {
                largestBettingDelta = bettingDelta;
                changedBettingPlayer = playerNumber;
            }

            int currentExchangeCount =
                GetExchangeDecisionCount(playerNumber);

            int exchangeDelta =
                currentExchangeCount -
                observedExchangeDecisionCounts[playerNumber];

            if (exchangeDelta > largestExchangeDelta)
            {
                largestExchangeDelta = exchangeDelta;
                changedExchangePlayer = playerNumber;
            }
        }
    }

    private AdviceTrigger DetermineTrigger(
        int changedBettingPlayer,
        int changedExchangePlayer)
    {
        if (gameManager.GameNumber != observedGameNumber)
        {
            return AdviceTrigger.NewHand;
        }

        if (gameManager.CurrentPhase == GamePhase.Showdown &&
            observedPhase != GamePhase.Showdown)
        {
            return AdviceTrigger.Showdown;
        }

        if (gameManager.CurrentPhase != observedPhase)
        {
            return AdviceTrigger.PhaseChanged;
        }

        bool humanTurnStarted =
            gameManager.CurrentTurnPlayer == humanPlayer &&
            observedTurnIndex != gameManager.CurrentTurnIndex;

        if (humanTurnStarted)
        {
            return AdviceTrigger.HumanTurn;
        }

        if (changedBettingPlayer > 0)
        {
            return AdviceTrigger.OpponentBettingAction;
        }

        if (changedExchangePlayer > 0)
        {
            return AdviceTrigger.OpponentExchange;
        }

        int currentSelectionHash =
            humanPlayer != null
                ? GetSelectionHash(humanPlayer)
                : 0;

        if (currentSelectionHash != observedSelectionHash &&
            gameManager.CurrentPhase == GamePhase.Exchange)
        {
            return AdviceTrigger.HumanExchangeSelection;
        }

        if (gameManager.CurrentTurnPlayer == humanPlayer)
        {
            return AdviceTrigger.HumanTurn;
        }

        return AdviceTrigger.StateChanged;
    }

    private int GetBettingActionCount(
        int playerNumber)
    {
        if (historyTracker == null)
        {
            return 0;
        }

        PokerPlayerHistory history =
            historyTracker.GetHistory(playerNumber);

        return history != null
            ? history.bettingActionCount
            : 0;
    }

    private int GetExchangeDecisionCount(
        int playerNumber)
    {
        if (historyTracker == null)
        {
            return 0;
        }

        PokerPlayerHistory history =
            historyTracker.GetHistory(playerNumber);

        return history != null
            ? history.exchangeDecisionCount
            : 0;
    }

    private int GetSelectionHash(
        PlayerControl player)
    {
        if (player == null ||
            player.selectedExchangeIndexes == null)
        {
            return 0;
        }

        int hash = 17;

        for (int i = 0;
             i < player.selectedExchangeIndexes.Count;
             i++)
        {
            hash =
                (hash * 31) +
                player.selectedExchangeIndexes[i] +
                1;
        }

        return hash;
    }

    private void RequestAdvice(
        AdviceTrigger trigger,
        int relatedPlayerNumber,
        bool immediate = false)
    {
        if (trigger == AdviceTrigger.None)
        {
            return;
        }

        adviceRequestVersion++;
        int requestVersion = adviceRequestVersion;

        if (pendingAdviceCoroutine != null)
        {
            StopCoroutine(pendingAdviceCoroutine);
        }

        pendingAdviceCoroutine =
            StartCoroutine(
                GenerateAdviceAfterDelay(
                    trigger,
                    relatedPlayerNumber,
                    requestVersion,
                    immediate
                )
            );
    }

    private IEnumerator GenerateAdviceAfterDelay(
        AdviceTrigger trigger,
        int relatedPlayerNumber,
        int requestVersion,
        bool immediate)
    {
        float requiredDelay =
            immediate ? 0f : Mathf.Max(0f, analysisDelay);

        float elapsed = 0f;

        while (elapsed < requiredDelay)
        {
            if (requestVersion != adviceRequestVersion)
            {
                yield break;
            }

            if (gameManager == null ||
                gameManager.IsHomeScreenOpen ||
                !gameManager.IsSessionActive)
            {
                yield break;
            }

            if (!gameManager.IsPaused)
            {
                elapsed += Time.unscaledDeltaTime;
            }

            yield return null;
        }

        float earliestDisplayTime =
            lastAdviceDisplayedTime +
            Mathf.Max(0f, minimumAdviceInterval);

        while (Time.unscaledTime < earliestDisplayTime)
        {
            if (requestVersion != adviceRequestVersion)
            {
                yield break;
            }

            yield return null;
        }

        if (requestVersion != adviceRequestVersion ||
            gameManager == null ||
            gameManager.IsPaused ||
            gameManager.IsHomeScreenOpen ||
            !gameManager.IsSessionActive)
        {
            yield break;
        }

        string advice =
            GenerateAdvice(
                trigger,
                relatedPlayerNumber
            );

        if (!string.IsNullOrWhiteSpace(advice))
        {
            DisplayAdvice(advice);
        }

        pendingAdviceCoroutine = null;
    }

    private string GenerateAdvice(
        AdviceTrigger trigger,
        int relatedPlayerNumber)
    {
        ResolveReferences();

        if (humanPlayer == null ||
            !humanPlayer.HasValidFiveCardHand())
        {
            return NormalizeAdvice(
                PickPhrase(
                    "카드를 확인하며 상대의 첫 행동까지 함께 분석하고 있어요.",
                    "패가 모두 들어오면 승산과 상대 성향을 함께 계산해드릴게요.",
                    "첫 베팅 전까지 상대 위치와 스택을 차분히 살펴볼게요."
                )
            );
        }

        if (gameManager.CurrentPhase == GamePhase.Showdown ||
            trigger == AdviceTrigger.Showdown)
        {
            return GenerateShowdownAdvice();
        }

        if (gameManager.CurrentPhase == GamePhase.Exchange)
        {
            if (trigger == AdviceTrigger.OpponentExchange &&
                relatedPlayerNumber > 0)
            {
                return GenerateOpponentExchangeAdvice(
                    relatedPlayerNumber
                );
            }

            if (gameManager.CurrentTurnPlayer == humanPlayer ||
                trigger == AdviceTrigger.HumanExchangeSelection)
            {
                return GenerateHumanExchangeAdvice();
            }

            return GenerateWaitingExchangeAdvice(
                relatedPlayerNumber
            );
        }

        if (gameManager.CurrentPhase == GamePhase.FirstBetting ||
            gameManager.CurrentPhase == GamePhase.FinalBetting)
        {
            if (gameManager.CurrentTurnPlayer == humanPlayer)
            {
                return GenerateHumanBettingAdvice(
                    relatedPlayerNumber
                );
            }

            if (trigger == AdviceTrigger.OpponentBettingAction &&
                relatedPlayerNumber > 0)
            {
                return GenerateOpponentBettingAdvice(
                    relatedPlayerNumber
                );
            }

            return GenerateWaitingBettingAdvice(
                relatedPlayerNumber
            );
        }

        return NormalizeAdvice(
            PickPhrase(
                "현재 패와 상대 기록을 함께 정리하고 있어요. 다음 선택을 준비해봐요.",
                "상대의 베팅 크기와 교환 장수를 계속 비교해볼게요.",
                "지금은 흐름을 읽는 구간이에요. 다음 행동까지 차분히 지켜봐요."
            )
        );
    }

    private string GenerateHumanBettingAdvice(
        int relatedPlayerNumber)
    {
        HandInsight hand =
            AnalyzeHand(humanPlayer.cardNumbers);

        PokerAIBettingContext context =
            CreateBettingContext();

        PokerAIBettingDecision decision =
            GetStableBettingDecision(context);

        PokerAIBettingOption bestOption =
            GetBestEvaluatedOption(decision);

        BettingAction recommendedAction =
            bestOption != null
                ? bestOption.action
                : decision.action;

        string actionName =
            GetActionName(recommendedAction);

        int equityPercent =
            Mathf.RoundToInt(
                Mathf.Clamp01(
                    decision.estimatedEquity
                ) * 100f
            );

        int potOddsPercent =
            Mathf.RoundToInt(
                Mathf.Clamp01(
                    decision.potOdds
                ) * 100f
            );

        string confidenceText =
            GetDecisionConfidenceText(
                decision.confidence
            );

        long callAmount =
            gameManager.GetCurrentCallAmount(
                humanPlayer
            );

        bool callWouldBeAllIn =
            callAmount > 0L &&
            callAmount >= humanPlayer.CurrentMoney;

        OpponentInsight opponent =
            FindMostRelevantOpponent(
                relatedPlayerNumber
            );

        List<AdviceCandidate> candidates =
            new List<AdviceCandidate>();

        string opponentName =
            opponent != null
                ? GetPlayerName(
                    opponent.player.playerNumber
                )
                : string.Empty;

        string patternText =
            opponent != null
                ? GetOpponentPatternText(opponent)
                : string.Empty;

        string exchangeTell =
            opponent != null
                ? GetExchangeTellText(opponent.player)
                : string.Empty;

        if (callWouldBeAllIn)
        {
            candidates.Add(
                new AdviceCandidate(
                    1001,
                    9.5f,
                    "콜하면 전액 승부예요. " +
                    WithAndParticle(hand.categoryName) +
                    " 예상 승산 " +
                    equityPercent +
                    "%를 보고 " +
                    WithObjectParticle(actionName) +
                    " 선택하세요."
                )
            );
        }

        if (callAmount > 0L)
        {
            candidates.Add(
                new AdviceCandidate(
                    1002,
                    8.8f,
                    "현재 패는 " +
                    WithCopula(hand.categoryName) +
                    " 콜 비용은 팟의 " +
                    potOddsPercent +
                    "%예요. 계산상 " +
                    WithTopicParticle(actionName) +
                    " 가장 좋아요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    1003,
                    8.4f,
                    "예상 승산은 " +
                    equityPercent +
                    "%예요. " +
                    confidenceText +
                    " 현재 가격과 패 강도를 함께 보면 " +
                    WithSubjectParticle(actionName) +
                    " 합리적이에요."
                )
            );
        }
        else
        {
            candidates.Add(
                new AdviceCandidate(
                    1004,
                    8.7f,
                    "현재 패는 " +
                    WithCopula(hand.categoryName) +
                    " 먼저 낼 비용은 없어요. " +
                    WithDirectionParticle(actionName) +
                    " 주도권을 잡아봐요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    1005,
                    8.2f,
                    "체크가 가능한 상황이에요. 예상 승산 " +
                    equityPercent +
                    "%를 기준으로 " +
                    WithSubjectParticle(actionName) +
                    " 가장 자연스러워요."
                )
            );
        }

        if (opponent != null)
        {
            if (opponent.read.bluffLikelihood >= 0.52f &&
                IsAggressiveAction(
                    opponent.read.lastBettingAction
                ))
            {
                candidates.Add(
                    new AdviceCandidate(
                        1010,
                        9.1f,
                        opponentName +
                        "님은 강하게 밀 때 블러프 비중도 있어요. " +
                        WithIfParticle(hand.categoryName) +
                        " " +
                        WithDirectionParticle(actionName) +
                        " 대응해볼 만해요."
                    )
                );
            }

            if (opponent.read.trapLikelihood >= 0.42f &&
                IsPassiveAction(
                    opponent.read.lastBettingAction
                ))
            {
                candidates.Add(
                    new AdviceCandidate(
                        1011,
                        9f,
                        opponentName +
                        "님은 조용히 강한 패를 숨긴 기록이 있어요. " +
                        WithObjectParticle(actionName) +
                        " 선택하되 큰 재압박은 경계하세요."
                    )
                );
            }

            if (opponent.hasReliableActionSample)
            {
                candidates.Add(
                    new AdviceCandidate(
                        1012,
                        8.5f,
                        opponentName +
                        "님은 " +
                        patternText +
                        " 편이에요. 현재 " +
                        hand.categoryName +
                        "에는 " +
                        WithSubjectParticle(actionName) +
                        " 좋아요."
                    )
                );
            }
            else
            {
                candidates.Add(
                    new AdviceCandidate(
                        1013,
                        8.3f,
                        opponentName +
                        "님 기록은 아직 적어요. 과신하지 말고 패 강도 기준으로 " +
                        WithObjectParticle(actionName) +
                        " 선택하세요."
                    )
                );
            }

            if (!string.IsNullOrEmpty(exchangeTell) &&
                gameManager.CurrentPhase ==
                GamePhase.FinalBetting)
            {
                candidates.Add(
                    new AdviceCandidate(
                        1014,
                        8.6f,
                        opponentName +
                        "님은 " +
                        exchangeTell +
                        "어요. 이 정보까지 반영하면 " +
                        WithTopicParticle(actionName) +
                        " 좋아요."
                    )
                );
            }
        }

        if (!string.IsNullOrEmpty(hand.drawText) &&
            gameManager.CurrentPhase ==
            GamePhase.FirstBetting)
        {
            candidates.Add(
                new AdviceCandidate(
                    1020,
                    8.65f,
                    hand.drawText +
                    " 가능성이 있어요. 교환 전 잠재력까지 보면 " +
                    WithDirectionParticle(actionName) +
                    " 이어가도 좋아요."
                )
            );
        }

        int activeOpponents =
            gameManager.GetActiveOpponentCount(
                humanPlayer
            );

        if (activeOpponents >= 3 &&
            hand.category <= PokerHandCategory.OnePair)
        {
            candidates.Add(
                new AdviceCandidate(
                    1021,
                    8.9f,
                    "상대가 " +
                    activeOpponents +
                    "명 남아 " +
                    hand.categoryName +
                    "의 상대 가치가 낮아요. 무리보다 " +
                    WithSubjectParticle(actionName) +
                    " 안전해요."
                )
            );
        }

        float positionScore =
            gameManager.GetAIPositionScore(
                humanPlayer
            );

        if (positionScore >= 0.68f)
        {
            candidates.Add(
                new AdviceCandidate(
                    1022,
                    8.1f,
                    "후반 순서라 상대 행동을 충분히 봤어요. 현재 정보에서는 " +
                    WithDirectionParticle(actionName) +
                    " 압박해도 좋아요."
                )
            );
        }
        else if (positionScore <= 0.32f)
        {
            candidates.Add(
                new AdviceCandidate(
                    1023,
                    8.05f,
                    "앞 순서라 뒤의 반응이 남아 있어요. " +
                    hand.categoryName +
                    " 기준으로 " +
                    WithObjectParticle(actionName) +
                    " 신중히 선택하세요."
                )
            );
        }

        string selected =
            SelectCandidateText(candidates);

        if (logAdviceAnalysis)
        {
            Debug.Log(
                "[AI 어시스트] 베팅 / 패=" +
                hand.categoryName +
                " / 승산=" +
                equityPercent +
                "% / 팟오즈=" +
                potOddsPercent +
                "% / 추천=" +
                actionName,
                this
            );
        }

        return NormalizeAdvice(selected);
    }

    private string GenerateOpponentBettingAdvice(
        int playerNumber)
    {
        OpponentInsight opponent =
            FindMostRelevantOpponent(
                playerNumber
            );

        HandInsight hand =
            AnalyzeHand(humanPlayer.cardNumbers);

        if (opponent == null)
        {
            return GenerateWaitingBettingAdvice(-1);
        }

        string name =
            GetPlayerName(playerNumber);

        BettingAction action =
            opponent.read.lastBettingAction;

        string actionName =
            GetActionName(action);

        string exchangeTell =
            GetExchangeTellText(opponent.player);

        List<AdviceCandidate> candidates =
            new List<AdviceCandidate>();

        if (action == BettingAction.Fold)
        {
            candidates.Add(
                new AdviceCandidate(
                    2001,
                    10f,
                    name +
                    "님이 다이해 상대가 줄었어요. " +
                    hand.categoryName +
                    "의 상대 가치는 조금 올라갔어요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    2002,
                    9f,
                    name +
                    "님이 빠졌어요. 남은 상대 수가 줄어 다음 압박 기회를 살펴봐요."
                )
            );
        }
        else if (IsAggressiveAction(action))
        {
            if (opponent.read.bluffLikelihood >= 0.52f &&
                opponent.hasReliableShowdownSample)
            {
                candidates.Add(
                    new AdviceCandidate(
                        2010,
                        10f,
                        name +
                        "님이 " +
                        WithDirectionParticle(actionName) +
                        " 압박했어요. 이전 쇼다운상 블러프 가능성도 열어두세요."
                    )
                );
            }

            if (opponent.read.bluffLikelihood <= 0.24f &&
                opponent.hasReliableShowdownSample)
            {
                candidates.Add(
                    new AdviceCandidate(
                        2011,
                        10f,
                        name +
                        "님은 허세가 적은 편인데 " +
                        actionName +
                        "했어요. 강한 패 비중을 높게 봐야 해요."
                    )
                );
            }

            candidates.Add(
                new AdviceCandidate(
                    2012,
                    9.1f,
                    name +
                    "님이 " +
                    actionName +
                    "했어요. " +
                    GetOpponentPatternText(opponent) +
                    " 성향과 베팅 크기를 함께 보세요."
                )
            );

            if (!string.IsNullOrEmpty(exchangeTell) &&
                gameManager.CurrentPhase ==
                GamePhase.FinalBetting)
            {
                candidates.Add(
                    new AdviceCandidate(
                        2013,
                        9.4f,
                        name +
                        "님은 " +
                        exchangeTell +
                        " 뒤 " +
                        actionName +
                        "했어요. 완성패 가능성을 더 경계하세요."
                    )
                );
            }
        }
        else
        {
            if (opponent.read.trapLikelihood >= 0.42f &&
                opponent.hasReliableShowdownSample)
            {
                candidates.Add(
                    new AdviceCandidate(
                        2020,
                        9.8f,
                        name +
                        "님이 " +
                        actionName +
                        "했지만 강한 패를 숨긴 기록이 있어요. 다음 재압박을 조심하세요."
                    )
                );
            }

            candidates.Add(
                new AdviceCandidate(
                    2021,
                    8.8f,
                    name +
                    "님이 " +
                    actionName +
                    "했어요. 약함으로 단정하지 말고 다음 베팅 크기까지 확인해봐요."
                )
            );

            if (opponent.read.aggressionRate >= 0.58f)
            {
                candidates.Add(
                    new AdviceCandidate(
                        2022,
                        9.2f,
                        "평소 공격적인 " +
                        name +
                        "님이 " +
                        actionName +
                        "했어요. 함정이나 포기 여부를 다음 행동에서 구분해봐요."
                    )
                );
            }
        }

        if (!opponent.hasReliableActionSample)
        {
            candidates.Add(
                new AdviceCandidate(
                    2030,
                    8.7f,
                    name +
                    "님의 표본은 아직 적어요. 이번 " +
                    actionName +
                    " 한 번만으로 성향을 단정하지 마세요."
                )
            );
        }

        string selected =
            SelectCandidateText(candidates);

        return NormalizeAdvice(selected);
    }

    private string GenerateWaitingBettingAdvice(
        int relatedPlayerNumber)
    {
        HandInsight hand =
            AnalyzeHand(humanPlayer.cardNumbers);

        OpponentInsight opponent =
            FindMostRelevantOpponent(
                relatedPlayerNumber
            );

        List<AdviceCandidate> candidates =
            new List<AdviceCandidate>();

        candidates.Add(
            new AdviceCandidate(
                3001,
                8f,
                "현재 패는 " +
                WithCopula(hand.categoryName) +
                " 상대의 다음 베팅 크기와 순서를 함께 지켜봐요."
            )
        );

        if (!string.IsNullOrEmpty(hand.drawText) &&
            gameManager.CurrentPhase ==
            GamePhase.FirstBetting)
        {
            candidates.Add(
                new AdviceCandidate(
                    3002,
                    8.6f,
                    hand.drawText +
                    " 가능성이 있어요. 교환 전에는 완성패뿐 아니라 개선 여지도 중요해요."
                )
            );
        }

        if (opponent != null)
        {
            string name =
                GetPlayerName(
                    opponent.player.playerNumber
                );

            candidates.Add(
                new AdviceCandidate(
                    3003,
                    8.5f,
                    name +
                    "님은 " +
                    GetOpponentPatternText(opponent) +
                    " 편이에요. 내 차례 전 행동 변화를 살펴봐요."
                )
            );

            if (opponent.read.currentWinStreak >= 2)
            {
                candidates.Add(
                    new AdviceCandidate(
                        3004,
                        8.8f,
                        name +
                        "님은 최근 " +
                        opponent.read.currentWinStreak +
                        "연승이에요. 자신감 있는 큰 베팅과 실제 강패를 구분해봐요."
                    )
                );
            }
        }

        return NormalizeAdvice(
            SelectCandidateText(candidates)
        );
    }

    private string GenerateHumanExchangeAdvice()
    {
        HandInsight hand =
            AnalyzeHand(humanPlayer.cardNumbers);

        PokerAIExchangeContext context =
            CreateExchangeContext();

        PokerAIExchangeDecision decision =
            GetStableExchangeDecision(
                context,
                hand
            );

        List<int> recommendedIndexes =
            NormalizeExchangeIndexes(
                decision != null
                    ? decision.exchangeIndexes
                    : null,
                gameManager.MaxExchangeCards
            );

        List<int> selectedIndexes =
            NormalizeExchangeIndexes(
                humanPlayer.selectedExchangeIndexes,
                gameManager.MaxExchangeCards
            );

        bool selectionMatches =
            AreIndexSetsEqual(
                selectedIndexes,
                recommendedIndexes
            );

        List<int> missingIndexes =
            GetIndexDifference(
                recommendedIndexes,
                selectedIndexes
            );

        List<int> extraIndexes =
            GetIndexDifference(
                selectedIndexes,
                recommendedIndexes
            );

        string recommendedText =
            FormatHandIndexes(
                recommendedIndexes
            );

        string selectedText =
            FormatHandIndexes(
                selectedIndexes
            );

        string missingText =
            FormatHandIndexes(
                missingIndexes
            );

        string extraText =
            FormatHandIndexes(
                extraIndexes
            );

        string strategyText =
            GetExchangeStrategyExplanation(
                hand,
                recommendedIndexes,
                decision != null
                    ? decision.reason
                    : string.Empty
            );

        List<AdviceCandidate> candidates =
            new List<AdviceCandidate>();

        if (recommendedIndexes.Count == 0)
        {
            if (selectedIndexes.Count == 0)
            {
                candidates.Add(
                    new AdviceCandidate(
                        4001,
                        11.2f,
                        WithTopicParticle(
                            hand.categoryName
                        ) +
                        " 유지 가치가 높아요. 교환하지 않는 편이 좋아요."
                    )
                );

                candidates.Add(
                    new AdviceCandidate(
                        4002,
                        10.7f,
                        strategyText
                    )
                );
            }
            else
            {
                candidates.Add(
                    new AdviceCandidate(
                        4003,
                        11.5f,
                        selectedText +
                        " 카드 선택을 취소하세요. AI 추천은 교환 없이 유지하는 거예요."
                    )
                );

                candidates.Add(
                    new AdviceCandidate(
                        4004,
                        10.8f,
                        WithTopicParticle(
                            hand.categoryName
                        ) +
                        " 이미 완성도가 높아요. 선택한 카드는 그대로 남겨두세요."
                    )
                );
            }
        }
        else if (selectionMatches)
        {
            candidates.Add(
                new AdviceCandidate(
                    4010,
                    12f,
                    "선택이 정확해요. 교환 대상은 계속 " +
                    recommendedText +
                    " 카드예요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    4011,
                    11.4f,
                    recommendedText +
                    " 카드를 고른 현재 선택이 합의 분석 결과와 같아요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    4012,
                    10.9f,
                    strategyText
                )
            );
        }
        else if (selectedIndexes.Count == 0)
        {
            candidates.Add(
                new AdviceCandidate(
                    4020,
                    11.8f,
                    "AI 추천 교환 대상은 " +
                    recommendedText +
                    " 카드예요. 같은 패에서는 추천이 바뀌지 않아요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    4021,
                    11.1f,
                    WithObjectParticle(
                        recommendedText
                    ) +
                    " 선택하세요. " +
                    strategyText
                )
            );
        }
        else if (extraIndexes.Count == 0 &&
                 missingIndexes.Count > 0)
        {
            candidates.Add(
                new AdviceCandidate(
                    4030,
                    12f,
                    selectedText +
                    " 카드까지는 맞아요. " +
                    missingText +
                    " 카드도 추가하면 추천과 같아요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    4031,
                    11.2f,
                    "현재 선택을 유지하고 " +
                    WithObjectParticle(
                        missingText
                    ) +
                    " 더 선택하세요."
                )
            );
        }
        else if (missingIndexes.Count == 0 &&
                 extraIndexes.Count > 0)
        {
            candidates.Add(
                new AdviceCandidate(
                    4040,
                    12f,
                    extraText +
                    " 카드는 남기는 편이 좋아요. 해당 선택만 취소하세요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    4041,
                    11.2f,
                    "추천 조합은 " +
                    recommendedText +
                    " 카드예요. " +
                    extraText +
                    " 카드 선택을 해제하세요."
                )
            );
        }
        else
        {
            candidates.Add(
                new AdviceCandidate(
                    4050,
                    12f,
                    extraText +
                    " 카드는 취소하고 " +
                    missingText +
                    " 카드를 선택하세요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    4051,
                    11.4f,
                    "추천 교환 대상은 계속 " +
                    recommendedText +
                    " 카드예요. 현재 선택을 그 조합에 맞춰주세요."
                )
            );
        }

        if (!string.IsNullOrEmpty(hand.drawText))
        {
            candidates.Add(
                new AdviceCandidate(
                    4060,
                    9.3f,
                    hand.drawText +
                    " 가능성을 보호한 추천이에요. 핵심 카드는 남겨두세요."
                )
            );
        }

        if (logAdviceAnalysis)
        {
            Debug.Log(
                "[AI 어시스트] 교환 / 패=" +
                hand.categoryName +
                " / 추천=" +
                recommendedText +
                " / 현재선택=" +
                selectedText +
                " / 근거=" +
                (decision != null
                    ? decision.reason
                    : "없음"),
                this
            );
        }

        return NormalizeAdvice(
            SelectCandidateText(candidates)
        );
    }

    private string GenerateOpponentExchangeAdvice(
        int playerNumber)
    {
        PlayerControl opponentPlayer =
            gameManager.GetPlayerByNumber(
                playerNumber
            );

        if (opponentPlayer == null)
        {
            return GenerateWaitingExchangeAdvice(-1);
        }

        OpponentInsight insight =
            FindMostRelevantOpponent(
                playerNumber
            );

        string name =
            GetPlayerName(playerNumber);

        int count =
            opponentPlayer.ExchangedCardCount;

        List<AdviceCandidate> candidates =
            new List<AdviceCandidate>();

        if (count <= 0)
        {
            candidates.Add(
                new AdviceCandidate(
                    5001,
                    10f,
                    name +
                    "님이 교환하지 않았어요. 완성패나 강한 블러프 준비 가능성을 함께 봐야 해요."
                )
            );

            if (insight != null &&
                insight.read.trapLikelihood >= 0.4f)
            {
                candidates.Add(
                    new AdviceCandidate(
                        5002,
                        10.2f,
                        name +
                        "님은 무교환 후 강한 패를 숨긴 기록이 있어요. 마지막 베팅을 특히 조심하세요."
                    )
                );
            }
        }
        else if (count == 1)
        {
            candidates.Add(
                new AdviceCandidate(
                    5010,
                    10f,
                    name +
                    "님이 1장만 바꿨어요. 투페어·트리플 보완이나 완성 직전 패 가능성이 있어요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    5011,
                    9.1f,
                    name +
                    "님의 1장 교환은 강한 기존 조합을 지켰다는 신호일 수 있어요."
                )
            );
        }
        else if (count == 2)
        {
            candidates.Add(
                new AdviceCandidate(
                    5020,
                    10f,
                    name +
                    "님이 2장을 바꿨어요. 원페어나 트리플 중심으로 패를 다듬었을 가능성이 있어요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    5021,
                    9f,
                    name +
                    "님의 2장 교환은 중간 강도 신호예요. 마지막 베팅 크기로 범위를 좁혀봐요."
                )
            );
        }
        else
        {
            candidates.Add(
                new AdviceCandidate(
                    5030,
                    10f,
                    name +
                    "님이 " +
                    count +
                    "장을 바꿨어요. 약한 시작패나 원페어 재구성 가능성이 비교적 높아요."
                )
            );

            candidates.Add(
                new AdviceCandidate(
                    5031,
                    9.1f,
                    name +
                    "님은 많은 카드를 바꿨어요. 교환 전 패는 약했을 가능성이 있지만 단정하진 마세요."
                )
            );
        }

        if (insight != null &&
            !insight.hasReliableActionSample)
        {
            candidates.Add(
                new AdviceCandidate(
                    5040,
                    8.8f,
                    name +
                    "님의 기록은 아직 적어요. 교환 장수는 참고만 하고 마지막 베팅을 더 중요하게 보세요."
                )
            );
        }

        return NormalizeAdvice(
            SelectCandidateText(candidates)
        );
    }

    private string GenerateWaitingExchangeAdvice(
        int relatedPlayerNumber)
    {
        HandInsight hand =
            AnalyzeHand(humanPlayer.cardNumbers);

        OpponentInsight opponent =
            FindMostRelevantOpponent(
                relatedPlayerNumber
            );

        List<AdviceCandidate> candidates =
            new List<AdviceCandidate>();

        candidates.Add(
            new AdviceCandidate(
                6001,
                8f,
                hand.categoryName +
                "의 핵심 조합을 확인했어요. 내 차례에는 약한 카드만 정리해봐요."
            )
        );

        if (!string.IsNullOrEmpty(hand.drawText))
        {
            candidates.Add(
                new AdviceCandidate(
                    6002,
                    8.7f,
                    hand.drawText +
                    " 가능성이 있어요. 관련 무늬나 연속 숫자는 남겨두는 편이 좋아요."
                )
            );
        }

        if (opponent != null &&
            opponent.player.HasExchangedThisGame)
        {
            candidates.Add(
                new AdviceCandidate(
                    6003,
                    8.9f,
                    GetPlayerName(
                        opponent.player.playerNumber
                    ) +
                    "님은 " +
                    opponent.player.ExchangedCardCount +
                    "장을 교환했어요. 마지막 베팅에서 이 신호를 다시 확인해요."
                )
            );
        }

        return NormalizeAdvice(
            SelectCandidateText(candidates)
        );
    }

    private string GenerateShowdownAdvice()
    {
        HandInsight hand =
            humanPlayer.HasValidFiveCardHand()
                ? AnalyzeHand(humanPlayer.cardNumbers)
                : null;

        if (humanPlayer.IsWinnerThisGame)
        {
            return NormalizeAdvice(
                PickPhrase(
                    "이번 판은 승리했어요. 상대의 교환과 베팅 패턴도 다음 판단에 반영할게요.",
                    "좋은 결과예요. 이번 쇼다운 정보로 상대 성향 분석이 더 정확해졌어요.",
                    "승리했어요. 어떤 상대가 압박하고 숨겼는지 다음 판에도 이어서 살펴볼게요."
                )
            );
        }

        if (humanPlayer.IsFolded)
        {
            return NormalizeAdvice(
                PickPhrase(
                    "이번 판은 일찍 정리했어요. 공개된 상대 패턴은 다음 판 판단에 활용할게요.",
                    "다이 선택도 좋은 방어가 될 수 있어요. 상대의 쇼다운 정보를 기억해둘게요.",
                    "이번 손실은 제한했어요. 다음 판에는 상대의 교환 신호까지 더 세밀히 볼게요."
                )
            );
        }

        string handName =
            hand != null
                ? hand.categoryName
                : "현재 패";

        return NormalizeAdvice(
            PickPhrase(
                WithDirectionParticle(handName) +
                " 승부했어요. 결과보다 상대 패턴을 다음 판에 반영하는 게 중요해요.",
                "이번 쇼다운 정보가 쌓였어요. 다음 판에는 블러프와 함정 판단이 더 정교해져요.",
                "결과를 확인했어요. 상대의 교환 장수와 마지막 베팅 조합을 기억해둘게요."
            )
        );
    }

    private PokerAIBettingContext CreateBettingContext()
    {
        PokerAIBettingContext context =
            new PokerAIBettingContext
            {
                gameNumber = gameManager.GameNumber,
                phase = gameManager.CurrentPhase,
                playerNumber = humanPlayer.playerNumber,
                seatIndex =
                    gameManager.GetSeatIndex(
                        humanPlayer
                    ),
                dealerIndex = gameManager.DealerIndex,
                smallBlindIndex =
                    gameManager.SmallBlindIndex,
                bigBlindIndex =
                    gameManager.BigBlindIndex,
                activeOpponentCount =
                    gameManager.GetActiveOpponentCount(
                        humanPlayer
                    ),
                positionScore =
                    gameManager.GetAIPositionScore(
                        humanPlayer
                    ),
                totalPot = gameManager.TotalPot,
                currentCallAmount =
                    gameManager.GetCurrentCallAmount(
                        humanPlayer
                    ),
                currentHighestBet =
                    gameManager.CurrentHighestBet,
                ownCurrentMoney =
                    humanPlayer.CurrentMoney,
                ownRoundBetMoney =
                    humanPlayer.RoundBetMoney,
                ownTotalBetThisGame =
                    humanPlayer.TotalBetThisGame,
                parameters =
                    CreateCoachParameters(),
                monteCarloSamples =
                    Mathf.Clamp(
                        bettingMonteCarloSamples,
                        40,
                        600
                    )
            };

        context.ownCards.AddRange(
            humanPlayer.cardNumbers
        );

        context.availableOptions =
            gameManager.GetAvailableAIBettingOptions(
                humanPlayer
            );

        FillPublicOpponentStates(
            context.opponents
        );

        if (historyTracker != null)
        {
            context.ownHistoryRead =
                historyTracker.GetRead(
                    humanPlayer.playerNumber
                );
        }

        return context;
    }

    private PokerAIExchangeContext CreateExchangeContext()
    {
        PokerAIExchangeContext context =
            new PokerAIExchangeContext
            {
                gameNumber = gameManager.GameNumber,
                playerNumber = humanPlayer.playerNumber,
                totalPot = gameManager.TotalPot,
                ownCurrentMoney =
                    humanPlayer.CurrentMoney,
                maxExchangeCards =
                    gameManager.MaxExchangeCards,
                parameters =
                    CreateCoachParameters(),
                monteCarloSamplesPerCandidate =
                    Mathf.Clamp(
                        exchangeMonteCarloSamplesPerCandidate,
                        60,
                        600
                    )
            };

        context.ownCards.AddRange(
            humanPlayer.cardNumbers
        );

        FillPublicOpponentStates(
            context.opponents
        );

        if (historyTracker != null)
        {
            context.ownHistoryRead =
                historyTracker.GetRead(
                    humanPlayer.playerNumber
                );
        }

        return context;
    }

    private PokerAIParameters CreateCoachParameters()
    {
        return new PokerAIParameters
        {
            style = PokerAIStyle.Calculated,
            aggression =
                Mathf.Clamp01(coachAggression),
            handSelectivity =
                Mathf.Clamp01(coachHandSelectivity),
            bluffTendency =
                Mathf.Clamp01(coachBluffTendency)
        };
    }

    private PokerAIBettingDecision GetStableBettingDecision(
        PokerAIBettingContext context)
    {
        if (context == null)
        {
            return new PokerAIBettingDecision
            {
                action = BettingAction.Check,
                reason = "베팅 분석 정보가 없습니다."
            };
        }

        string key =
            BuildBettingDecisionKey(context);

        if (cachedBettingDecision != null &&
            cachedBettingDecisionKey == key)
        {
            return cachedBettingDecision;
        }

        int seed =
            ComputeStableSeed(
                key,
                1733
            );

        cachedBettingDecision =
            PokerAIBrain.DecideBetting(
                context,
                new System.Random(seed)
            );

        cachedBettingDecisionKey = key;

        return cachedBettingDecision;
    }

    private PokerAIExchangeDecision GetStableExchangeDecision(
        PokerAIExchangeContext context,
        HandInsight hand)
    {
        if (context == null)
        {
            return new PokerAIExchangeDecision
            {
                reason = "교환 분석 정보가 없습니다."
            };
        }

        string key =
            BuildExchangeDecisionKey(context);

        if (keepExchangeRecommendationStable &&
            cachedExchangeDecision != null &&
            cachedExchangeDecisionKey == key)
        {
            return cachedExchangeDecision;
        }

        int passes =
            Mathf.Clamp(
                exchangeConsensusPasses,
                1,
                5
            );

        Dictionary<string, ExchangeConsensusRecord> records =
            new Dictionary<string, ExchangeConsensusRecord>();

        for (int pass = 0;
             pass < passes;
             pass++)
        {
            int seed =
                ComputeStableSeed(
                    key,
                    7919 * (pass + 1)
                );

            PokerAIExchangeDecision passDecision =
                PokerAIBrain.DecideExchange(
                    context,
                    new System.Random(seed)
                );

            List<int> normalized =
                NormalizeExchangeIndexes(
                    passDecision != null
                        ? passDecision.exchangeIndexes
                        : null,
                    context.maxExchangeCards
                );

            string indexKey =
                BuildIndexSetKey(normalized);

            ExchangeConsensusRecord record;

            if (!records.TryGetValue(
                    indexKey,
                    out record))
            {
                record =
                    new ExchangeConsensusRecord
                    {
                        indexes =
                            new List<int>(normalized)
                    };

                records.Add(
                    indexKey,
                    record
                );
            }

            float passConfidence =
                passDecision != null
                    ? Mathf.Clamp01(
                        passDecision.confidence
                      )
                    : 0f;

            float passExpectedScore =
                passDecision != null
                    ? Mathf.Clamp01(
                        passDecision.expectedScore
                      )
                    : 0f;

            record.votes++;
            record.weightedScore +=
                1f +
                (passConfidence * 0.7f) +
                (passExpectedScore * 0.25f);

            record.expectedScoreTotal +=
                passExpectedScore;

            record.confidenceTotal +=
                passConfidence;
        }

        ExchangeConsensusRecord consensus =
            SelectBestExchangeConsensus(records);

        List<int> finalIndexes =
            consensus != null
                ? new List<int>(consensus.indexes)
                : new List<int>();

        string finalReason =
            consensus != null
                ? "다중 시드 합의 분석"
                : "안전한 무교환 판단";

        float finalExpectedScore =
            consensus != null &&
            consensus.votes > 0
                ? consensus.expectedScoreTotal /
                  consensus.votes
                : 0f;

        float finalConfidence =
            consensus != null
                ? Mathf.Clamp01(
                    (consensus.votes /
                     (float)Mathf.Max(1, passes)) *
                    0.72f +
                    (consensus.confidenceTotal /
                     Mathf.Max(1, consensus.votes)) *
                    0.28f
                  )
                : 0.5f;

        List<int> strategicIndexes;
        string strategicReason;

        if (useStrategicExchangeGuardrails &&
            TryGetStrategicExchangeIndexes(
                hand,
                context.ownCards,
                context.maxExchangeCards,
                out strategicIndexes,
                out strategicReason
            ))
        {
            bool consensusAgrees =
                AreIndexSetsEqual(
                    finalIndexes,
                    strategicIndexes
                );

            finalIndexes =
                NormalizeExchangeIndexes(
                    strategicIndexes,
                    context.maxExchangeCards
                );

            finalReason =
                strategicReason +
                (consensusAgrees
                    ? " / 합의 분석 일치"
                    : " / 패 구조 보호 우선");

            finalConfidence =
                consensusAgrees
                    ? Mathf.Max(
                        finalConfidence,
                        0.9f
                      )
                    : Mathf.Max(
                        finalConfidence,
                        0.78f
                      );
        }

        cachedExchangeDecision =
            new PokerAIExchangeDecision
            {
                exchangeIndexes =
                    new List<int>(finalIndexes),
                expectedScore =
                    finalExpectedScore,
                confidence =
                    Mathf.Clamp01(finalConfidence),
                reason = finalReason
            };

        cachedExchangeDecisionKey = key;

        return cachedExchangeDecision;
    }

    private ExchangeConsensusRecord SelectBestExchangeConsensus(
        Dictionary<string, ExchangeConsensusRecord> records)
    {
        ExchangeConsensusRecord best = null;

        foreach (KeyValuePair<string, ExchangeConsensusRecord> pair
                 in records)
        {
            ExchangeConsensusRecord current =
                pair.Value;

            if (current == null)
            {
                continue;
            }

            if (best == null ||
                current.votes > best.votes ||
                (current.votes == best.votes &&
                 current.weightedScore >
                 best.weightedScore))
            {
                best = current;
            }
        }

        return best;
    }

    private bool TryGetStrategicExchangeIndexes(
        HandInsight hand,
        IList<int> cards,
        int maxExchange,
        out List<int> indexes,
        out string reason)
    {
        indexes = new List<int>();
        reason = string.Empty;

        if (hand == null ||
            cards == null ||
            cards.Count != 5)
        {
            return false;
        }

        maxExchange =
            Mathf.Clamp(
                maxExchange,
                0,
                5
            );

        if (hand.category >=
            PokerHandCategory.Straight)
        {
            reason =
                "완성된 " +
                hand.categoryName +
                " 유지";

            return true;
        }

        int[] rankCounts = new int[13];
        int[] suitCounts = new int[4];

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            int card = cards[i];

            if (!CardUtility.IsValidCardNumber(card))
            {
                continue;
            }

            rankCounts[
                (int)CardUtility.GetRank(card)
            ]++;

            suitCounts[
                (int)CardUtility.GetSuit(card)
            ]++;
        }

        if (hand.category ==
            PokerHandCategory.ThreeOfAKind)
        {
            int tripRank =
                FindRankWithCount(
                    rankCounts,
                    3
                );

            indexes =
                FindIndexesNotMatchingRank(
                    cards,
                    tripRank
                );

            indexes =
                LimitExchangeIndexes(
                    indexes,
                    cards,
                    maxExchange
                );

            reason =
                "트리플을 남기고 두 장 교환";

            return true;
        }

        if (hand.category ==
            PokerHandCategory.TwoPair)
        {
            int kickerRank =
                FindRankWithCount(
                    rankCounts,
                    1
                );

            indexes =
                FindIndexesMatchingRank(
                    cards,
                    kickerRank
                );

            indexes =
                LimitExchangeIndexes(
                    indexes,
                    cards,
                    maxExchange
                );

            reason =
                "투페어를 남기고 킥커 교환";

            return true;
        }

        if (hand.category ==
            PokerHandCategory.OnePair)
        {
            int pairRank =
                FindRankWithCount(
                    rankCounts,
                    2
                );

            indexes =
                FindIndexesNotMatchingRank(
                    cards,
                    pairRank
                );

            indexes =
                LimitExchangeIndexes(
                    indexes,
                    cards,
                    maxExchange
                );

            reason =
                "원페어를 남기고 나머지 카드 교환";

            return true;
        }

        if (hand.category ==
            PokerHandCategory.HighCard)
        {
            List<int> flushIndexes =
                GetFourFlushExchangeIndexes(
                    cards,
                    suitCounts
                );

            List<int> straightIndexes =
                GetFourStraightExchangeIndexes(
                    cards
                );

            if (flushIndexes != null &&
                straightIndexes != null)
            {
                if (AreIndexSetsEqual(
                        flushIndexes,
                        straightIndexes))
                {
                    indexes =
                        LimitExchangeIndexes(
                            flushIndexes,
                            cards,
                            maxExchange
                        );

                    reason =
                        "스트레이트와 플러시 동시 드로우 보호";

                    return true;
                }

                // 두 드로우가 서로 다른 카드를 요구하면
                // 몬테카를로 합의 결과가 더 적합하므로 강제하지 않습니다.
                return false;
            }

            if (flushIndexes != null)
            {
                indexes =
                    LimitExchangeIndexes(
                        flushIndexes,
                        cards,
                        maxExchange
                    );

                reason =
                    "4플러시를 남기고 한 장 교환";

                return true;
            }

            if (straightIndexes != null)
            {
                indexes =
                    LimitExchangeIndexes(
                        straightIndexes,
                        cards,
                        maxExchange
                    );

                reason =
                    "4스트레이트를 남기고 한 장 교환";

                return true;
            }
        }

        return false;
    }

    private int FindRankWithCount(
        int[] rankCounts,
        int targetCount)
    {
        if (rankCounts == null)
        {
            return -1;
        }

        for (int rank =
                 rankCounts.Length - 1;
             rank >= 0;
             rank--)
        {
            if (rankCounts[rank] ==
                targetCount)
            {
                return rank;
            }
        }

        return -1;
    }

    private List<int> FindIndexesMatchingRank(
        IList<int> cards,
        int targetRank)
    {
        List<int> result =
            new List<int>();

        if (cards == null ||
            targetRank < 0)
        {
            return result;
        }

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            int card = cards[i];

            if (CardUtility.IsValidCardNumber(card) &&
                (int)CardUtility.GetRank(card) ==
                targetRank)
            {
                result.Add(i);
            }
        }

        return result;
    }

    private List<int> FindIndexesNotMatchingRank(
        IList<int> cards,
        int protectedRank)
    {
        List<int> result =
            new List<int>();

        if (cards == null)
        {
            return result;
        }

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            int card = cards[i];

            if (!CardUtility.IsValidCardNumber(card) ||
                (int)CardUtility.GetRank(card) !=
                protectedRank)
            {
                result.Add(i);
            }
        }

        return result;
    }

    private List<int> GetFourFlushExchangeIndexes(
        IList<int> cards,
        int[] suitCounts)
    {
        if (cards == null ||
            suitCounts == null)
        {
            return null;
        }

        int protectedSuit = -1;

        for (int suit = 0;
             suit < suitCounts.Length;
             suit++)
        {
            if (suitCounts[suit] == 4)
            {
                protectedSuit = suit;
                break;
            }
        }

        if (protectedSuit < 0)
        {
            return null;
        }

        List<int> result =
            new List<int>();

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            int card = cards[i];

            if (!CardUtility.IsValidCardNumber(card) ||
                (int)CardUtility.GetSuit(card) !=
                protectedSuit)
            {
                result.Add(i);
            }
        }

        return result.Count == 1
            ? result
            : null;
    }

    private List<int> GetFourStraightExchangeIndexes(
        IList<int> cards)
    {
        if (cards == null ||
            cards.Count != 5)
        {
            return null;
        }

        int bestWindowHigh = -999;
        List<int> bestKeptIndexes = null;

        for (int start = -1;
             start <= 8;
             start++)
        {
            List<int> keptIndexes =
                new List<int>();

            HashSet<int> usedRanks =
                new HashSet<int>();

            for (int i = 0;
                 i < cards.Count;
                 i++)
            {
                int card = cards[i];

                if (!CardUtility.IsValidCardNumber(card))
                {
                    continue;
                }

                int rank =
                    (int)CardUtility.GetRank(card);

                int normalizedRank =
                    rank == 12 &&
                    start == -1
                        ? -1
                        : rank;

                if (normalizedRank >= start &&
                    normalizedRank <= start + 4 &&
                    !usedRanks.Contains(
                        normalizedRank))
                {
                    usedRanks.Add(
                        normalizedRank
                    );

                    keptIndexes.Add(i);
                }
            }

            if (keptIndexes.Count != 4)
            {
                continue;
            }

            int windowHigh =
                start + 4;

            if (bestKeptIndexes == null ||
                windowHigh > bestWindowHigh)
            {
                bestWindowHigh = windowHigh;
                bestKeptIndexes = keptIndexes;
            }
        }

        if (bestKeptIndexes == null)
        {
            return null;
        }

        List<int> result =
            new List<int>();

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            if (!bestKeptIndexes.Contains(i))
            {
                result.Add(i);
            }
        }

        return result.Count == 1
            ? result
            : null;
    }

    private List<int> LimitExchangeIndexes(
        IList<int> indexes,
        IList<int> cards,
        int maxExchange)
    {
        List<int> result =
            NormalizeExchangeIndexes(
                indexes,
                5
            );

        maxExchange =
            Mathf.Clamp(
                maxExchange,
                0,
                5
            );

        result.Sort(
            delegate (int a, int b)
            {
                int rankA =
                    GetCardRankSafe(cards, a);

                int rankB =
                    GetCardRankSafe(cards, b);

                int rankCompare =
                    rankA.CompareTo(rankB);

                return rankCompare != 0
                    ? rankCompare
                    : a.CompareTo(b);
            }
        );

        if (result.Count > maxExchange)
        {
            result.RemoveRange(
                maxExchange,
                result.Count - maxExchange
            );
        }

        result.Sort();

        return result;
    }

    private int GetCardRankSafe(
        IList<int> cards,
        int handIndex)
    {
        if (cards == null ||
            handIndex < 0 ||
            handIndex >= cards.Count ||
            !CardUtility.IsValidCardNumber(
                cards[handIndex]))
        {
            return 99;
        }

        return (int)CardUtility.GetRank(
            cards[handIndex]
        );
    }

    private List<int> NormalizeExchangeIndexes(
        IList<int> indexes,
        int maxExchange)
    {
        List<int> result =
            new List<int>();

        if (indexes != null)
        {
            for (int i = 0;
                 i < indexes.Count;
                 i++)
            {
                int index = indexes[i];

                if (index < 0 ||
                    index >= 5 ||
                    result.Contains(index))
                {
                    continue;
                }

                result.Add(index);
            }
        }

        result.Sort();

        maxExchange =
            Mathf.Clamp(
                maxExchange,
                0,
                5
            );

        if (result.Count > maxExchange)
        {
            result.RemoveRange(
                maxExchange,
                result.Count - maxExchange
            );
        }

        return result;
    }

    private string BuildBettingDecisionKey(
        PokerAIBettingContext context)
    {
        StringBuilder builder =
            new StringBuilder(384);

        builder.Append(context.gameNumber)
            .Append('|')
            .Append((int)context.phase)
            .Append('|')
            .Append(context.playerNumber)
            .Append('|')
            .Append(context.seatIndex)
            .Append('|')
            .Append(context.dealerIndex)
            .Append('|')
            .Append(context.activeOpponentCount)
            .Append('|')
            .Append(context.totalPot)
            .Append('|')
            .Append(context.currentCallAmount)
            .Append('|')
            .Append(context.currentHighestBet)
            .Append('|')
            .Append(context.ownCurrentMoney)
            .Append('|')
            .Append(context.ownRoundBetMoney)
            .Append('|')
            .Append(context.ownTotalBetThisGame)
            .Append('|')
            .Append(context.monteCarloSamples)
            .Append('|')
            .Append(
                Mathf.RoundToInt(
                    context.positionScore *
                    1000f
                )
            )
            .Append('|')
            .Append(
                Mathf.RoundToInt(
                    coachAggression *
                    1000f
                )
            )
            .Append('|')
            .Append(
                Mathf.RoundToInt(
                    coachHandSelectivity *
                    1000f
                )
            )
            .Append('|')
            .Append(
                Mathf.RoundToInt(
                    coachBluffTendency *
                    1000f
                )
            );

        AppendCardKey(
            builder,
            context.ownCards
        );

        if (context.availableOptions != null)
        {
            for (int i = 0;
                 i < context.availableOptions.Count;
                 i++)
            {
                PokerAIBettingOption option =
                    context.availableOptions[i];

                if (option == null)
                {
                    continue;
                }

                builder.Append("|O:")
                    .Append((int)option.action)
                    .Append(':')
                    .Append(option.additionalAmount)
                    .Append(':')
                    .Append(option.targetRoundBet);
            }
        }

        if (context.opponents != null)
        {
            for (int i = 0;
                 i < context.opponents.Count;
                 i++)
            {
                PokerAIPublicPlayerState opponent =
                    context.opponents[i];

                if (opponent == null)
                {
                    continue;
                }

                builder.Append("|P:")
                    .Append(opponent.playerNumber)
                    .Append(':')
                    .Append(opponent.isFolded ? 1 : 0)
                    .Append(':')
                    .Append(opponent.isAllIn ? 1 : 0)
                    .Append(':')
                    .Append(opponent.hasExchanged ? 1 : 0)
                    .Append(':')
                    .Append(opponent.exchangedCardCount)
                    .Append(':')
                    .Append(opponent.currentMoney)
                    .Append(':')
                    .Append(opponent.roundBetMoney)
                    .Append(':')
                    .Append(opponent.totalBetThisGame);

                AppendOpponentReadKey(
                    builder,
                    opponent.historyRead
                );
            }
        }

        return builder.ToString();
    }

    private string BuildExchangeDecisionKey(
        PokerAIExchangeContext context)
    {
        StringBuilder builder =
            new StringBuilder(160);

        builder.Append(context.gameNumber)
            .Append('|')
            .Append(context.playerNumber)
            .Append('|')
            .Append(context.maxExchangeCards)
            .Append('|')
            .Append(
                context.monteCarloSamplesPerCandidate
            )
            .Append('|')
            .Append(exchangeConsensusPasses)
            .Append('|')
            .Append(
                useStrategicExchangeGuardrails
                    ? 1
                    : 0
            )
            .Append('|')
            .Append(
                Mathf.RoundToInt(
                    coachAggression *
                    1000f
                )
            )
            .Append('|')
            .Append(
                Mathf.RoundToInt(
                    coachHandSelectivity *
                    1000f
                )
            )
            .Append('|')
            .Append(
                Mathf.RoundToInt(
                    coachBluffTendency *
                    1000f
                )
            );

        AppendCardKey(
            builder,
            context.ownCards
        );

        return builder.ToString();
    }

    private void AppendCardKey(
        StringBuilder builder,
        IList<int> cards)
    {
        builder.Append("|C");

        if (cards == null)
        {
            return;
        }

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            builder.Append(':')
                .Append(cards[i]);
        }
    }

    private void AppendOpponentReadKey(
        StringBuilder builder,
        PokerAIOpponentRead read)
    {
        if (read == null)
        {
            builder.Append(":R0");
            return;
        }

        builder.Append(":R")
            .Append(read.handsObserved)
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.foldRate *
                    1000f
                )
            )
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.callRate *
                    1000f
                )
            )
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.raiseRate *
                    1000f
                )
            )
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.aggressionRate *
                    1000f
                )
            )
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.noExchangeRate *
                    1000f
                )
            )
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.bluffLikelihood *
                    1000f
                )
            )
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.trapLikelihood *
                    1000f
                )
            )
            .Append(':')
            .Append(
                Mathf.RoundToInt(
                    read.recentMomentum *
                    1000f
                )
            )
            .Append(':')
            .Append((int)read.lastBettingAction)
            .Append(':')
            .Append(read.lastBettingGameNumber);
    }

    private string BuildIndexSetKey(
        IList<int> indexes)
    {
        if (indexes == null ||
            indexes.Count == 0)
        {
            return "-";
        }

        StringBuilder builder =
            new StringBuilder();

        for (int i = 0;
             i < indexes.Count;
             i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(indexes[i]);
        }

        return builder.ToString();
    }

    private int ComputeStableSeed(
        string key,
        int salt)
    {
        unchecked
        {
            uint hash = 2166136261u;

            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0;
                     i < key.Length;
                     i++)
                {
                    hash ^= key[i];
                    hash *= 16777619u;
                }
            }

            hash ^= (uint)salt;
            hash *= 16777619u;

            return (int)(
                hash & 0x7FFFFFFF
            );
        }
    }

    private void ClearDecisionCaches()
    {
        cachedBettingDecisionKey =
            string.Empty;

        cachedBettingDecision = null;

        cachedExchangeDecisionKey =
            string.Empty;

        cachedExchangeDecision = null;
    }

    private void FillPublicOpponentStates(
        List<PokerAIPublicPlayerState> destination)
    {
        destination.Clear();

        for (int i = 0;
             i < gameManager.players.Count;
             i++)
        {
            PlayerControl opponent =
                gameManager.players[i];

            if (opponent == null ||
                opponent == humanPlayer)
            {
                continue;
            }

            PokerAIPublicPlayerState state =
                new PokerAIPublicPlayerState
                {
                    playerNumber =
                        opponent.playerNumber,
                    seatIndex = i,
                    isHuman =
                        opponent.IsHumanPlayer,
                    isFolded =
                        opponent.IsFolded,
                    isAllIn =
                        opponent.IsAllIn,
                    hasExchanged =
                        opponent.HasExchangedThisGame,
                    exchangedCardCount =
                        opponent.ExchangedCardCount,
                    currentMoney =
                        opponent.CurrentMoney,
                    roundBetMoney =
                        opponent.RoundBetMoney,
                    totalBetThisGame =
                        opponent.TotalBetThisGame,
                    historyRead =
                        historyTracker != null
                            ? historyTracker.GetRead(
                                opponent.playerNumber
                              )
                            : null
                };

            destination.Add(state);
        }
    }

    private PokerAIBettingOption GetBestEvaluatedOption(
        PokerAIBettingDecision decision)
    {
        if (decision == null ||
            decision.evaluatedOptions == null ||
            decision.evaluatedOptions.Count == 0)
        {
            return null;
        }

        PokerAIBettingOption best = null;

        for (int i = 0;
             i < decision.evaluatedOptions.Count;
             i++)
        {
            PokerAIBettingOption option =
                decision.evaluatedOptions[i];

            if (option == null)
            {
                continue;
            }

            if (best == null ||
                option.utility > best.utility)
            {
                best = option;
            }
        }

        return best;
    }

    private HandInsight AnalyzeHand(
        IList<int> cards)
    {
        HandInsight result =
            new HandInsight
            {
                category =
                    PokerHandCategory.HighCard,
                categoryName = "하이카드",
                keyRankName = string.Empty,
                drawText = string.Empty
            };

        if (cards == null ||
            cards.Count != 5)
        {
            return result;
        }

        result.value =
            PokerHandEvaluator.Evaluate(cards);

        result.category =
            result.value.Category;

        result.categoryName =
            GetHandCategoryName(
                result.category
            );

        result.normalizedStrength =
            PokerAIBrain.GetNormalizedHandScore(
                result.value
            );

        int[] rankCounts = new int[13];
        int[] suitCounts = new int[4];

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            int card = cards[i];

            if (!CardUtility.IsValidCardNumber(card))
            {
                continue;
            }

            int rank =
                (int)CardUtility.GetRank(card);

            int suit =
                (int)CardUtility.GetSuit(card);

            rankCounts[rank]++;
            suitCounts[suit]++;
        }

        int highestGroupRank = -1;

        for (int rank = 0;
             rank < rankCounts.Length;
             rank++)
        {
            int count = rankCounts[rank];

            result.maximumSameRankCount =
                Mathf.Max(
                    result.maximumSameRankCount,
                    count
                );

            if (count == 2)
            {
                result.pairCount++;
            }

            if (count >= 2 &&
                rank > highestGroupRank)
            {
                highestGroupRank = rank;
            }
        }

        if (highestGroupRank >= 0)
        {
            result.keyRankName =
                GetRankName(highestGroupRank);
        }

        for (int suit = 0;
             suit < suitCounts.Length;
             suit++)
        {
            if (suitCounts[suit] == 4)
            {
                result.hasFourFlush = true;
                break;
            }
        }

        result.hasFourStraight =
            HasFourCardStraightPotential(
                rankCounts
            );

        if (result.hasFourFlush &&
            result.hasFourStraight)
        {
            result.drawText =
                "플러시와 스트레이트 양쪽";
        }
        else if (result.hasFourFlush)
        {
            result.drawText =
                "플러시 완성";
        }
        else if (result.hasFourStraight)
        {
            result.drawText =
                "스트레이트 완성";
        }
        else if (result.category ==
                 PokerHandCategory.OnePair)
        {
            result.drawText =
                result.keyRankName +
                " 원페어 강화";
        }
        else if (result.category ==
                 PokerHandCategory.TwoPair)
        {
            result.drawText =
                "풀하우스 완성";
        }
        else if (result.category ==
                 PokerHandCategory.ThreeOfAKind)
        {
            result.drawText =
                "풀하우스나 포카드 완성";
        }

        return result;
    }

    private bool HasFourCardStraightPotential(
        int[] rankCounts)
    {
        if (rankCounts == null ||
            rankCounts.Length < 13)
        {
            return false;
        }

        HashSet<int> ranks =
            new HashSet<int>();

        for (int rank = 0;
             rank < 13;
             rank++)
        {
            if (rankCounts[rank] > 0)
            {
                ranks.Add(rank);
            }
        }

        // A를 가장 낮은 카드로도 사용할 수 있도록 -1을 추가합니다.
        if (ranks.Contains(12))
        {
            ranks.Add(-1);
        }

        for (int start = -1;
             start <= 8;
             start++)
        {
            int contained = 0;

            for (int offset = 0;
                 offset < 5;
                 offset++)
            {
                if (ranks.Contains(start + offset))
                {
                    contained++;
                }
            }

            if (contained >= 4)
            {
                return true;
            }
        }

        return false;
    }

    private OpponentInsight FindMostRelevantOpponent(
        int preferredPlayerNumber)
    {
        OpponentInsight best = null;

        for (int i = 0;
             i < gameManager.players.Count;
             i++)
        {
            PlayerControl player =
                gameManager.players[i];

            if (player == null ||
                player == humanPlayer)
            {
                continue;
            }

            // 방금 다이한 플레이어의 행동도 조언에 반영해야 하므로,
            // 지정된 상대는 Fold 상태여도 분석 대상에 포함합니다.
            if (player.IsFolded &&
                player.playerNumber !=
                preferredPlayerNumber)
            {
                continue;
            }

            PokerAIOpponentRead read =
                historyTracker != null
                    ? historyTracker.GetRead(
                        player.playerNumber
                      )
                    : new PokerAIOpponentRead
                    {
                        playerNumber =
                              player.playerNumber
                    };

            PokerPlayerHistory history =
                historyTracker != null
                    ? historyTracker.GetHistory(
                        player.playerNumber
                      )
                    : null;

            float score = 0f;

            if (player.playerNumber ==
                preferredPlayerNumber)
            {
                score += 4f;
            }

            if (read.hasLastBettingAction &&
                read.lastBettingGameNumber ==
                gameManager.GameNumber)
            {
                score += 1.3f;

                if (IsAggressiveAction(
                        read.lastBettingAction))
                {
                    score += 1f;
                }
            }

            score +=
                read.aggressionRate * 0.9f;

            score +=
                read.bluffLikelihood * 0.55f;

            score +=
                read.trapLikelihood * 0.65f;

            score +=
                Mathf.Max(
                    0f,
                    read.recentMomentum
                ) * 0.4f;

            if (player.IsAllIn)
            {
                score += 0.8f;
            }

            if (player.HasExchangedThisGame)
            {
                score +=
                    Mathf.Max(
                        0f,
                        3 - player.ExchangedCardCount
                    ) * 0.18f;
            }

            OpponentInsight insight =
                new OpponentInsight
                {
                    player = player,
                    read = read,
                    history = history,
                    relevanceScore = score,
                    hasReliableActionSample =
                        history != null &&
                        history.bettingActionCount >= 6,
                    hasReliableShowdownSample =
                        history != null &&
                        history.showdowns >= 3
                };

            if (best == null ||
                insight.relevanceScore >
                best.relevanceScore)
            {
                best = insight;
            }
        }

        return best;
    }

    private string GetOpponentPatternText(
        OpponentInsight opponent)
    {
        if (opponent == null ||
            opponent.read == null)
        {
            return "아직 파악 중인";
        }

        PokerAIOpponentRead read =
            opponent.read;

        if (!opponent.hasReliableActionSample)
        {
            return "아직 표본이 적은";
        }

        if (read.aggressionRate >= 0.62f &&
            read.bluffLikelihood >= 0.46f)
        {
            return "공격과 블러프를 자주 섞는";
        }

        if (read.aggressionRate >= 0.62f)
        {
            return "레이즈 비중이 높은";
        }

        if (read.foldRate >= 0.46f)
        {
            return "압박에 자주 접는";
        }

        if (read.callRate >= 0.48f)
        {
            return "콜로 끝까지 확인하는";
        }

        if (read.trapLikelihood >= 0.4f)
        {
            return "강한 패를 숨기는";
        }

        if (read.noExchangeRate >= 0.38f)
        {
            return "무교환을 자주 택하는";
        }

        return "균형 있게 대응하는";
    }

    private string GetExchangeTellText(
        PlayerControl opponent)
    {
        if (opponent == null ||
            !opponent.HasExchangedThisGame)
        {
            return string.Empty;
        }

        int count =
            opponent.ExchangedCardCount;

        if (count <= 0)
        {
            return "교환하지 않았";
        }

        if (count == 1)
        {
            return "1장만 바꿨";
        }

        if (count == 2)
        {
            return "2장을 바꿨";
        }

        return count + "장을 바꿨";
    }

    private string SelectCandidateText(
        List<AdviceCandidate> candidates)
    {
        if (candidates == null ||
            candidates.Count == 0)
        {
            return "현재 정보를 계속 분석하고 있어요. 다음 행동을 차분히 확인해봐요.";
        }

        candidates.Sort(
            delegate (
                AdviceCandidate a,
                AdviceCandidate b)
            {
                return b.score.CompareTo(a.score);
            }
        );

        List<AdviceCandidate> eligible =
            new List<AdviceCandidate>();

        int candidateLimit =
            Mathf.Min(5, candidates.Count);

        for (int i = 0;
             i < candidateLimit;
             i++)
        {
            AdviceCandidate candidate =
                candidates[i];

            if (candidate == null ||
                string.IsNullOrWhiteSpace(
                    candidate.text))
            {
                continue;
            }

            if (ContainsRecentTemplate(
                    candidate.templateId))
            {
                continue;
            }

            string normalized =
                NormalizeAdvice(candidate.text);

            if (ContainsRecentMessage(normalized))
            {
                continue;
            }

            eligible.Add(candidate);
        }

        if (eligible.Count == 0)
        {
            for (int i = 0;
                 i < candidateLimit;
                 i++)
            {
                if (candidates[i] != null)
                {
                    eligible.Add(candidates[i]);
                }
            }
        }

        float totalWeight = 0f;

        for (int i = 0;
             i < eligible.Count;
             i++)
        {
            float relativeScore =
                eligible[i].score -
                eligible[eligible.Count - 1].score;

            totalWeight +=
                Mathf.Max(
                    0.2f,
                    1f + relativeScore
                );
        }

        float pick =
            NextFloat() *
            Mathf.Max(0.001f, totalWeight);

        AdviceCandidate selected =
            eligible[0];

        for (int i = 0;
             i < eligible.Count;
             i++)
        {
            float relativeScore =
                eligible[i].score -
                eligible[eligible.Count - 1].score;

            pick -=
                Mathf.Max(
                    0.2f,
                    1f + relativeScore
                );

            if (pick <= 0f)
            {
                selected = eligible[i];
                break;
            }
        }

        RememberTemplate(
            selected.templateId
        );

        return selected.text;
    }

    private void DisplayAdvice(string advice)
    {
        string normalized =
            NormalizeAdvice(advice);

        if (adviceText != null)
        {
            adviceText.text = normalized;
        }

        lastAdviceDisplayedTime =
            Time.unscaledTime;

        RememberMessage(normalized);
    }

    private string NormalizeAdvice(
        string advice)
    {
        if (string.IsNullOrWhiteSpace(advice))
        {
            advice = "현재 정보를 분석하고 있어요.";
        }

        advice =
            advice.Replace("\r", " ")
                  .Replace("\n", " ");

        while (advice.Contains("  "))
        {
            advice =
                advice.Replace("  ", " ");
        }

        advice = advice.Trim();

        advice =
            RepairCommonKoreanParticleMistakes(
                advice
            );

        if (!EndsPolitely(advice))
        {
            advice =
                advice.TrimEnd(
                    '.', '!', '?', ' '
                ) + "요.";
        }

        int limit =
            Mathf.Clamp(
                maximumCharacters,
                40,
                100
            );

        if (advice.Length <= limit)
        {
            return advice;
        }

        int sentenceEnd =
            advice.LastIndexOf(
                '.',
                limit - 1
            );

        if (sentenceEnd >= 20)
        {
            return advice.Substring(
                0,
                sentenceEnd + 1
            );
        }

        int cutLength =
            Mathf.Max(1, limit - 3);

        int spaceIndex =
            advice.LastIndexOf(
                ' ',
                cutLength
            );

        if (spaceIndex >= 15)
        {
            cutLength = spaceIndex;
        }

        string shortened =
            advice.Substring(
                0,
                cutLength
            ).TrimEnd(
                ',', '.', '!', '?', ' '
            );

        return shortened + "요.";
    }

    private bool EndsPolitely(string text)
    {
        return text.EndsWith("요.") ||
               text.EndsWith("요!") ||
               text.EndsWith("요?") ||
               text.EndsWith("세요.") ||
               text.EndsWith("봐요.") ||
               text.EndsWith("해요.") ||
               text.EndsWith("이에요.") ||
               text.EndsWith("예요.");
    }

    private bool ContainsRecentTemplate(
        int templateId)
    {
        foreach (int recentId
                 in recentTemplateIds)
        {
            if (recentId == templateId)
            {
                return true;
            }
        }

        return false;
    }

    private void RememberTemplate(
        int templateId)
    {
        recentTemplateIds.Enqueue(
            templateId
        );

        int capacity =
            Mathf.Clamp(
                recentTemplateMemory,
                2,
                12
            );

        while (recentTemplateIds.Count >
               capacity)
        {
            recentTemplateIds.Dequeue();
        }
    }

    private bool ContainsRecentMessage(
        string message)
    {
        foreach (string recent
                 in recentMessages)
        {
            if (recent == message)
            {
                return true;
            }
        }

        return false;
    }

    private void RememberMessage(
        string message)
    {
        recentMessages.Enqueue(message);

        int capacity =
            Mathf.Clamp(
                recentMessageMemory,
                2,
                12
            );

        while (recentMessages.Count >
               capacity)
        {
            recentMessages.Dequeue();
        }
    }

    private void CancelPendingAdvice()
    {
        adviceRequestVersion++;

        if (pendingAdviceCoroutine != null)
        {
            StopCoroutine(
                pendingAdviceCoroutine
            );

            pendingAdviceCoroutine = null;
        }
    }

    private bool AreIndexSetsEqual(
        IList<int> a,
        IList<int> b)
    {
        int aCount =
            a != null ? a.Count : 0;

        int bCount =
            b != null ? b.Count : 0;

        if (aCount != bCount)
        {
            return false;
        }

        if (aCount == 0)
        {
            return true;
        }

        List<int> aCopy =
            new List<int>(a);

        List<int> bCopy =
            new List<int>(b);

        aCopy.Sort();
        bCopy.Sort();

        for (int i = 0;
             i < aCopy.Count;
             i++)
        {
            if (aCopy[i] != bCopy[i])
            {
                return false;
            }
        }

        return true;
    }

    private List<int> GetIndexDifference(
        IList<int> source,
        IList<int> subtract)
    {
        List<int> result =
            new List<int>();

        if (source == null)
        {
            return result;
        }

        for (int i = 0;
             i < source.Count;
             i++)
        {
            int value = source[i];

            if (subtract == null ||
                !subtract.Contains(value))
            {
                result.Add(value);
            }
        }

        result.Sort();

        return result;
    }

    private string GetExchangeStrategyExplanation(
        HandInsight hand,
        IList<int> recommendedIndexes,
        string decisionReason)
    {
        if (hand == null)
        {
            return "여러 교환 후보를 비교한 결과예요.";
        }

        if (recommendedIndexes == null ||
            recommendedIndexes.Count == 0)
        {
            return WithTopicParticle(
                       hand.categoryName
                   ) +
                   " 깨지 않고 유지하는 편이 기대값이 높아요.";
        }

        switch (hand.category)
        {
            case PokerHandCategory.OnePair:
                return "원페어를 남기고 나머지 세 장을 바꾸는 기본 전략이에요.";

            case PokerHandCategory.TwoPair:
                return "투페어는 킥커 한 장만 바꿔 풀하우스를 노리는 편이 좋아요.";

            case PokerHandCategory.ThreeOfAKind:
                return "트리플을 남기고 나머지 두 장만 바꿔 더 강한 패를 노려요.";
        }

        if (hand.hasFourFlush &&
            hand.hasFourStraight)
        {
            return "스트레이트와 플러시 가능성을 함께 보호한 선택이에요.";
        }

        if (hand.hasFourFlush)
        {
            return "같은 무늬 네 장을 남겨 플러시 완성 확률을 살리는 선택이에요.";
        }

        if (hand.hasFourStraight)
        {
            return "연속된 네 장을 남겨 스트레이트 완성 가능성을 살리는 선택이에요.";
        }

        if (!string.IsNullOrEmpty(decisionReason) &&
            decisionReason.Contains("합의"))
        {
            return exchangeConsensusPasses +
                   "회 독립 분석에서 가장 자주 선택된 교환 조합이에요.";
        }

        return "가능한 교환 조합의 기대값과 변동성을 함께 비교한 결과예요.";
    }

    private string GetDecisionConfidenceText(
        float confidence)
    {
        confidence =
            Mathf.Clamp01(confidence);

        if (confidence >= 0.72f)
        {
            return "분석 결과도 비교적 뚜렷해요.";
        }

        if (confidence >= 0.46f)
        {
            return "선택 간 차이는 크지 않아요.";
        }

        return "경계선 판단이라 상대 반응도 함께 봐야 해요.";
    }

    private string WithSubjectParticle(
        string word)
    {
        return AddKoreanParticle(
            word,
            "이",
            "가"
        );
    }

    private string WithObjectParticle(
        string word)
    {
        return AddKoreanParticle(
            word,
            "을",
            "를"
        );
    }

    private string WithTopicParticle(
        string word)
    {
        return AddKoreanParticle(
            word,
            "은",
            "는"
        );
    }

    private string WithAndParticle(
        string word)
    {
        return AddKoreanParticle(
            word,
            "과",
            "와"
        );
    }

    private string WithDirectionParticle(
        string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        int finalConsonant =
            GetKoreanFinalConsonantIndex(
                word
            );

        // 받침이 없거나 ㄹ 받침이면 '로', 그 외에는 '으로'를 사용합니다.
        string particle =
            finalConsonant == 0 ||
            finalConsonant == 8
                ? "로"
                : "으로";

        return word + particle;
    }

    private string WithIfParticle(
        string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        return word +
               (HasKoreanFinalConsonant(word)
                    ? "이라면"
                    : "라면");
    }

    private string WithCopula(
        string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        return word +
               (HasKoreanFinalConsonant(word)
                    ? "이에요."
                    : "예요.");
    }

    private string AddKoreanParticle(
        string word,
        string consonantParticle,
        string vowelParticle)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        return word +
               (HasKoreanFinalConsonant(word)
                    ? consonantParticle
                    : vowelParticle);
    }

    private bool HasKoreanFinalConsonant(
        string word)
    {
        return GetKoreanFinalConsonantIndex(
                   word
               ) > 0;
    }

    private int GetKoreanFinalConsonantIndex(
        string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return 0;
        }

        char lastCharacter =
            word[word.Length - 1];

        const int hangulStart = 0xAC00;
        const int hangulEnd = 0xD7A3;

        if (lastCharacter < hangulStart ||
            lastCharacter > hangulEnd)
        {
            return 0;
        }

        return
            (lastCharacter - hangulStart) %
            28;
    }

    private string RepairCommonKoreanParticleMistakes(
        string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // 템플릿 수정 누락이 생겨도 UI에서 어색한 조사가 보이지 않도록
        // 받침이 없는 주요 포커 용어의 흔한 오류를 마지막에 한 번 더 교정합니다.
        string[] vowelEndingWords =
        {
            "다이",
            "체크",
            "쿼터",
            "하프",
            "맥스",
            "하이카드",
            "원페어",
            "투페어",
            "스트레이트",
            "플러시",
            "풀하우스",
            "포카드",
            "스트레이트 플러시"
        };

        for (int i = 0;
             i < vowelEndingWords.Length;
             i++)
        {
            string word =
                vowelEndingWords[i];

            text =
                text.Replace(
                    word + "이 합리적",
                    word + "가 합리적"
                );

            text =
                text.Replace(
                    word + "이 좋아",
                    word + "가 좋아"
                );

            text =
                text.Replace(
                    word + "이 안전",
                    word + "가 안전"
                );

            text =
                text.Replace(
                    word + "을 선택",
                    word + "를 선택"
                );

            text =
                text.Replace(
                    word + "은 유지",
                    word + "는 유지"
                );

            text =
                text.Replace(
                    word + "으로 ",
                    word + "로 "
                );

            text =
                text.Replace(
                    word + "이면 ",
                    word + "라면 "
                );

            text =
                text.Replace(
                    word + "이에요",
                    word + "예요"
                );
        }

        return text;
    }

    private string FormatHandIndexes(
        IList<int> indexes)
    {
        if (indexes == null ||
            indexes.Count == 0)
        {
            return "교환 없음";
        }

        List<int> sorted =
            new List<int>(indexes);

        sorted.Sort();

        StringBuilder builder =
            new StringBuilder();

        for (int i = 0;
             i < sorted.Count;
             i++)
        {
            if (i > 0)
            {
                builder.Append('·');
            }

            builder.Append(
                sorted[i] + 1
            );
        }

        builder.Append("번째");

        return builder.ToString();
    }

    private string GetPlayerName(
        int playerNumber)
    {
        EnsurePlayerNames();

        if (playerNumber >= 0 &&
            playerNumber < playerNames.Length &&
            !string.IsNullOrWhiteSpace(
                playerNames[playerNumber]))
        {
            return playerNames[playerNumber];
        }

        return "플레이어 " +
               playerNumber;
    }

    private string GetActionName(
        BettingAction action)
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
                return "신중한 선택";
        }
    }

    private bool IsAggressiveAction(
        BettingAction action)
    {
        return action == BettingAction.Ping ||
               action == BettingAction.Double ||
               action == BettingAction.Quarter ||
               action == BettingAction.Half ||
               action == BettingAction.AllIn ||
               action == BettingAction.Max;
    }

    private bool IsPassiveAction(
        BettingAction action)
    {
        return action == BettingAction.Check ||
               action == BettingAction.Call;
    }

    private string GetHandCategoryName(
        PokerHandCategory category)
    {
        switch (category)
        {
            case PokerHandCategory.HighCard:
                return "하이카드";

            case PokerHandCategory.OnePair:
                return "원페어";

            case PokerHandCategory.TwoPair:
                return "투페어";

            case PokerHandCategory.ThreeOfAKind:
                return "트리플";

            case PokerHandCategory.Straight:
                return "스트레이트";

            case PokerHandCategory.Flush:
                return "플러시";

            case PokerHandCategory.FullHouse:
                return "풀하우스";

            case PokerHandCategory.FourOfAKind:
                return "포카드";

            case PokerHandCategory.StraightFlush:
                return "스트레이트 플러시";

            default:
                return "현재 패";
        }
    }

    private string GetRankName(int rank)
    {
        string[] names =
        {
            "2", "3", "4", "5", "6", "7", "8",
            "9", "10", "J", "Q", "K", "A"
        };

        if (rank < 0 ||
            rank >= names.Length)
        {
            return string.Empty;
        }

        return names[rank];
    }

    private string PickPhrase(
        params string[] phrases)
    {
        if (phrases == null ||
            phrases.Length == 0)
        {
            return string.Empty;
        }

        int index =
            random != null
                ? random.Next(0, phrases.Length)
                : UnityEngine.Random.Range(
                    0,
                    phrases.Length
                );

        return phrases[index];
    }

    private void InitializeRandom()
    {
        int seed =
            Environment.TickCount ^
            GetInstanceID() ^
            DateTime.Now.Millisecond;

        random =
            new System.Random(seed);
    }

    private float NextFloat()
    {
        if (random == null)
        {
            InitializeRandom();
        }

        return (float)random.NextDouble();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        stateCheckInterval =
            Mathf.Max(
                0.05f,
                stateCheckInterval
            );

        analysisDelay =
            Mathf.Max(0f, analysisDelay);

        minimumAdviceInterval =
            Mathf.Max(
                0f,
                minimumAdviceInterval
            );

        maximumCharacters =
            Mathf.Clamp(
                maximumCharacters,
                40,
                100
            );

        bettingMonteCarloSamples =
            Mathf.Clamp(
                bettingMonteCarloSamples,
                40,
                600
            );

        exchangeMonteCarloSamplesPerCandidate =
            Mathf.Clamp(
                exchangeMonteCarloSamplesPerCandidate,
                60,
                600
            );

        exchangeConsensusPasses =
            Mathf.Clamp(
                exchangeConsensusPasses,
                1,
                5
            );

        coachAggression =
            Mathf.Clamp01(
                coachAggression
            );

        coachHandSelectivity =
            Mathf.Clamp01(
                coachHandSelectivity
            );

        coachBluffTendency =
            Mathf.Clamp01(
                coachBluffTendency
            );

        recentTemplateMemory =
            Mathf.Clamp(
                recentTemplateMemory,
                2,
                12
            );

        recentMessageMemory =
            Mathf.Clamp(
                recentMessageMemory,
                2,
                12
            );

        EnsurePlayerNames();
    }
#endif
}