using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PlayerControl 1~4에 붙는 컴퓨터 플레이어 실행기입니다.
/// 실제 판단은 PokerAIBrain이 담당하고, 이 클래스는 턴 대기와
/// GameManager 호출만 담당합니다.
/// </summary>
[RequireComponent(typeof(PlayerControl))]
public class FiveCardDrawAI : MonoBehaviour
{
    [Header("AI Enable")]
    [Tooltip("이 컴퓨터 플레이어의 고급 AI를 사용합니다.")]
    public bool enableAI = true;

    [Header("Human-like Thinking Delay")]
    [Tooltip("행동을 결정하기 전 최소 대기 시간입니다.")]
    [Min(0f)]
    public float minimumThinkingDelay = 0.55f;

    [Tooltip("행동을 결정하기 전 최대 대기 시간입니다.")]
    [Min(0f)]
    public float maximumThinkingDelay = 1.25f;

    [Tooltip("큰 콜이나 어려운 판단일수록 추가되는 고민 시간입니다.")]
    [Min(0f)]
    public float difficultDecisionExtraDelay = 0.45f;

    [Header("Decision Accuracy / Performance")]
    [Tooltip("베팅 승률 추정에 사용할 몬테카를로 횟수입니다. 높을수록 정확하지만 연산량이 증가합니다.")]
    [Range(40, 600)]
    public int bettingMonteCarloSamples = 160;

    [Tooltip("교환 후보 하나당 새 카드 결과를 시험할 횟수입니다.")]
    [Range(60, 600)]
    public int exchangeMonteCarloSamplesPerCandidate = 180;

    [Header("Debug")]
    [Tooltip("AI가 판단한 승률, 팟오즈, 상대 분석과 행동 이유를 Console에 출력합니다.")]
    public bool logDecisionDetails = true;

    [Tooltip("0이면 실행할 때마다 다른 난수를 사용합니다. 같은 값이면 테스트 재현이 쉬워집니다.")]
    public int fixedRandomSeed;

    private PlayerControl player;
    private FiveCardDrawGameManager gameManager;
    private Coroutine turnCoroutine;
    private System.Random random;
    private int turnRequestVersion;

    private void Awake()
    {
        player = GetComponent<PlayerControl>();
        InitializeRandom();
    }

    private void OnDisable()
    {
        CancelPendingTurn();
    }

    /// <summary>
    /// GameManager가 이 플레이어의 턴을 시작할 때 호출합니다.
    /// </summary>
    public void BeginTurn(
        FiveCardDrawGameManager manager,
        PlayerControl owner,
        GamePhase expectedPhase)
    {
        if (!enableAI || manager == null || owner == null)
        {
            return;
        }

        if (!owner.IsComputerPlayer)
        {
            return;
        }

        gameManager = manager;
        player = owner;

        CancelPendingTurn();

        turnRequestVersion++;
        int requestVersion = turnRequestVersion;

        turnCoroutine = StartCoroutine(
            ThinkAndActRoutine(
                expectedPhase,
                requestVersion
            )
        );
    }

    public void CancelPendingTurn()
    {
        turnRequestVersion++;

        if (turnCoroutine != null)
        {
            StopCoroutine(turnCoroutine);
            turnCoroutine = null;
        }
    }

    private IEnumerator ThinkAndActRoutine(
        GamePhase expectedPhase,
        int requestVersion)
    {
        float delay = CalculateThinkingDelay(expectedPhase);

        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }
        else
        {
            yield return null;
        }

        turnCoroutine = null;

        if (!IsTurnStillValid(expectedPhase, requestVersion))
        {
            yield break;
        }

        try
        {
            if (expectedPhase == GamePhase.FirstBetting ||
                expectedPhase == GamePhase.FinalBetting)
            {
                ExecuteBettingDecision(expectedPhase);
            }
            else if (expectedPhase == GamePhase.Exchange)
            {
                ExecuteExchangeDecision();
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Player " + player.playerNumber +
                " AI 판단 오류: " + exception
            );

            ExecuteSafeFallback(expectedPhase);
        }
    }

    private float CalculateThinkingDelay(GamePhase phase)
    {
        float minDelay = Mathf.Max(0f, minimumThinkingDelay);
        float maxDelay = Mathf.Max(minDelay, maximumThinkingDelay);

        float delay = Mathf.Lerp(
            minDelay,
            maxDelay,
            NextFloat()
        );

        if (gameManager == null || player == null)
        {
            return delay;
        }

        if (phase == GamePhase.FirstBetting ||
            phase == GamePhase.FinalBetting)
        {
            long callAmount =
                gameManager.GetCurrentCallAmount(player);

            float callPressure =
                player.CurrentMoney > 0L
                    ? callAmount /
                      (float)player.CurrentMoney
                    : 1f;

            float potPressure =
                gameManager.TotalPot > 0L
                    ? callAmount /
                      (float)gameManager.TotalPot
                    : 0f;

            float difficulty = Mathf.Clamp01(
                callPressure * 0.75f +
                potPressure * 0.25f
            );

            delay +=
                difficulty *
                Mathf.Max(0f, difficultDecisionExtraDelay);

            // 계산형은 어려운 상황에서 조금 더 오래 생각합니다.
            if (player.AIStyle == PokerAIStyle.Calculated)
            {
                delay += difficulty * 0.18f;
            }

            // 공격형은 상대적으로 빠르게 압박합니다.
            if (player.AIStyle == PokerAIStyle.Aggressive)
            {
                delay *= 0.88f;
            }
        }
        else if (phase == GamePhase.Exchange)
        {
            delay += 0.12f;
        }

        return Mathf.Max(0f, delay);
    }

    private bool IsTurnStillValid(
        GamePhase expectedPhase,
        int requestVersion)
    {
        return enableAI &&
               gameManager != null &&
               player != null &&
               requestVersion == turnRequestVersion &&
               gameManager.CurrentPhase == expectedPhase &&
               gameManager.CurrentTurnPlayer == player &&
               !gameManager.IsGameFinished;
    }

    private void ExecuteBettingDecision(
        GamePhase phase)
    {
        PokerAIBettingContext context =
            CreateBettingContext(phase);

        PokerAIBettingDecision decision =
            PokerAIBrain.DecideBetting(
                context,
                random
            );

        BettingAction action = decision.action;

        if (!gameManager.IsBettingActionAvailable(
                player,
                action))
        {
            action = GetSafeBettingFallback();
        }

        if (logDecisionDetails || gameManager.logAIDecisions)
        {
            Debug.Log(
                "[AI Player " + player.playerNumber +
                " / " + player.AIStyle +
                "] " + decision.reason +
                " / 확신=" +
                Mathf.RoundToInt(decision.confidence * 100f) + "%"
            );
        }

        gameManager.SubmitBettingAction(player, action);
    }

    private void ExecuteExchangeDecision()
    {
        PokerAIExchangeContext context =
            CreateExchangeContext();

        PokerAIExchangeDecision decision =
            PokerAIBrain.DecideExchange(
                context,
                random
            );

        if (logDecisionDetails || gameManager.logAIDecisions)
        {
            Debug.Log(
                "[AI Player " + player.playerNumber +
                " / " + player.AIStyle +
                "] " + decision.reason +
                " / 확신=" +
                Mathf.RoundToInt(decision.confidence * 100f) + "%"
            );
        }

        gameManager.SubmitExchange(
            player,
            decision.exchangeIndexes
        );
    }

    private PokerAIBettingContext CreateBettingContext(
        GamePhase phase)
    {
        PokerAIBettingContext context =
            new PokerAIBettingContext
            {
                gameNumber = gameManager.GameNumber,
                phase = phase,
                playerNumber = player.playerNumber,
                seatIndex = gameManager.GetSeatIndex(player),
                dealerIndex = gameManager.DealerIndex,
                smallBlindIndex = gameManager.SmallBlindIndex,
                bigBlindIndex = gameManager.BigBlindIndex,
                activeOpponentCount =
                    gameManager.GetActiveOpponentCount(player),
                positionScore =
                    gameManager.GetAIPositionScore(player),
                totalPot = gameManager.TotalPot,
                currentCallAmount =
                    gameManager.GetCurrentCallAmount(player),
                currentHighestBet =
                    gameManager.CurrentHighestBet,
                ownCurrentMoney = player.CurrentMoney,
                ownRoundBetMoney = player.RoundBetMoney,
                ownTotalBetThisGame =
                    player.TotalBetThisGame,
                parameters = player.GetAIParameters(),
                monteCarloSamples =
                    bettingMonteCarloSamples
            };

        context.ownCards.AddRange(player.cardNumbers);
        context.availableOptions =
            gameManager.GetAvailableAIBettingOptions(player);

        FillPublicOpponentStates(
            context.opponents
        );

        PokerAIHistoryTracker tracker =
            gameManager.AIHistoryTracker;

        if (tracker != null)
        {
            context.ownHistoryRead =
                tracker.GetRead(player.playerNumber);
        }

        return context;
    }

    private PokerAIExchangeContext CreateExchangeContext()
    {
        PokerAIExchangeContext context =
            new PokerAIExchangeContext
            {
                gameNumber = gameManager.GameNumber,
                playerNumber = player.playerNumber,
                totalPot = gameManager.TotalPot,
                ownCurrentMoney = player.CurrentMoney,
                maxExchangeCards =
                    gameManager.MaxExchangeCards,
                parameters = player.GetAIParameters(),
                monteCarloSamplesPerCandidate =
                    exchangeMonteCarloSamplesPerCandidate
            };

        context.ownCards.AddRange(player.cardNumbers);

        FillPublicOpponentStates(
            context.opponents
        );

        PokerAIHistoryTracker tracker =
            gameManager.AIHistoryTracker;

        if (tracker != null)
        {
            context.ownHistoryRead =
                tracker.GetRead(player.playerNumber);
        }

        return context;
    }

    /// <summary>
    /// 상대 카드 번호는 전달하지 않고 공개된 금액, 폴드/올인,
    /// 교환 장수와 누적 행동 분석만 전달합니다.
    /// </summary>
    private void FillPublicOpponentStates(
        List<PokerAIPublicPlayerState> destination)
    {
        destination.Clear();

        PokerAIHistoryTracker tracker =
            gameManager.AIHistoryTracker;

        for (int i = 0; i < gameManager.players.Count; i++)
        {
            PlayerControl opponent =
                gameManager.players[i];

            if (opponent == null || opponent == player)
            {
                continue;
            }

            PokerAIPublicPlayerState state =
                new PokerAIPublicPlayerState
                {
                    playerNumber = opponent.playerNumber,
                    seatIndex = i,
                    isHuman = opponent.IsHumanPlayer,
                    isFolded = opponent.IsFolded,
                    isAllIn = opponent.IsAllIn,
                    hasExchanged =
                        opponent.HasExchangedThisGame,
                    exchangedCardCount =
                        opponent.ExchangedCardCount,
                    currentMoney = opponent.CurrentMoney,
                    roundBetMoney = opponent.RoundBetMoney,
                    totalBetThisGame =
                        opponent.TotalBetThisGame,
                    historyRead = tracker != null
                        ? tracker.GetRead(opponent.playerNumber)
                        : null
                };

            destination.Add(state);
        }
    }

    private BettingAction GetSafeBettingFallback()
    {
        if (gameManager.IsBettingActionAvailable(
                player,
                BettingAction.Check))
        {
            return BettingAction.Check;
        }

        if (gameManager.IsBettingActionAvailable(
                player,
                BettingAction.Call))
        {
            return BettingAction.Call;
        }

        return BettingAction.Fold;
    }

    private void ExecuteSafeFallback(GamePhase phase)
    {
        if (gameManager == null || player == null ||
            gameManager.CurrentTurnPlayer != player)
        {
            return;
        }

        if (phase == GamePhase.FirstBetting ||
            phase == GamePhase.FinalBetting)
        {
            gameManager.SubmitBettingAction(
                player,
                GetSafeBettingFallback()
            );
        }
        else if (phase == GamePhase.Exchange)
        {
            gameManager.SubmitExchange(
                player,
                new List<int>()
            );
        }
    }

    private void InitializeRandom()
    {
        int seed;

        if (fixedRandomSeed != 0)
        {
            seed = fixedRandomSeed +
                   (player != null
                       ? player.playerNumber * 7919
                       : 0);
        }
        else
        {
            seed = Environment.TickCount ^
                   GetInstanceID() ^
                   DateTime.Now.Millisecond;
        }

        random = new System.Random(seed);
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
        minimumThinkingDelay =
            Mathf.Max(0f, minimumThinkingDelay);

        maximumThinkingDelay =
            Mathf.Max(
                minimumThinkingDelay,
                maximumThinkingDelay
            );

        difficultDecisionExtraDelay =
            Mathf.Max(0f, difficultDecisionExtraDelay);

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
    }
#endif
}