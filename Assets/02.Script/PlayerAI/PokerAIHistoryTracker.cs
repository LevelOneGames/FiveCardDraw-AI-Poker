using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 번의 공개 베팅 행동 기록입니다.
/// 상대 AI는 이 기록과 공개된 교환 장수, 쇼다운 결과만 사용합니다.
/// </summary>
[Serializable]
public class PokerAIRecentActionRecord
{
    public int gameNumber;
    public GamePhase phase;
    public BettingAction action;
    public long callAmountBeforeAction;
    public long paidAmount;
    public long potBeforeAction;
    public long highestBetBeforeAction;
}

/// <summary>
/// 플레이어 한 명의 누적 공개 플레이 기록입니다.
/// </summary>
[Serializable]
public class PokerPlayerHistory
{
    public int playerNumber;

    [Header("Hands")]
    public int handsObserved;
    public int wins;
    public int showdowns;
    public int showdownWins;
    public long cumulativeNetChange;
    public long lastHandNetChange;
    public int currentWinStreak;
    public int currentLossStreak;

    [Header("Betting")]
    public int bettingActionCount;
    public int foldCount;
    public int checkCount;
    public int callCount;
    public int raiseCount;
    public int allInCount;
    public int firstBettingRaiseCount;
    public int finalBettingRaiseCount;
    public long totalPaidByActions;
    public long totalCallPaid;
    public long totalRaisePaid;

    [Header("Exchange")]
    public int exchangeDecisionCount;
    public int noExchangeCount;
    public int totalExchangedCards;
    public int lastExchangeCount;

    [Header("Showdown Read")]
    public int aggressiveShowdownCount;
    public int weakAggressiveShowdownCount;
    public int strongPassiveShowdownCount;
    public int revealedHandCount;
    public PokerHandCategory lastRevealedCategory;

    [Header("Last Public Action")]
    public bool hasLastBettingAction;
    public BettingAction lastBettingAction;
    public int lastBettingGameNumber = -1;
    public GamePhase lastBettingPhase;
    public long lastPaidAmount;
    public long lastCallAmountBeforeAction;
    public long lastPotBeforeAction;
    public long lastHighestBetBeforeAction;

    [Header("Recent Actions")]
    public List<PokerAIRecentActionRecord> recentActions =
        new List<PokerAIRecentActionRecord>();
}

/// <summary>
/// 현재 진행 중인 한 판에서만 사용하는 내부 기록입니다.
/// </summary>
internal class PokerAICurrentHandRecord
{
    public int gameNumber;
    public long startingMoney;
    public bool madeAggressiveAction;
    public bool madeAggressiveActionInFinalBetting;
    public bool madePassiveActionWithStrongPotential;
    public bool reachedShowdown;
    public int exchangeCount = -1;
}

/// <summary>
/// 모든 플레이어의 공개 행동을 판 단위로 누적합니다.
/// AI는 이 데이터를 통해 상대의 폴드율, 공격성, 블러프 가능성,
/// 무교환 성향, 최근 승패 흐름 등을 추정합니다.
/// </summary>
public class PokerAIHistoryTracker : MonoBehaviour
{
    [Header("History Capacity")]
    [Min(5)]
    public int recentActionCapacityPerPlayer = 24;

    [Header("Runtime History")]
    [SerializeField]
    private List<PokerPlayerHistory> playerHistories =
        new List<PokerPlayerHistory>();

    private readonly Dictionary<int, PokerAICurrentHandRecord>
        currentHandRecords =
            new Dictionary<int, PokerAICurrentHandRecord>();

    private int currentGameNumber = -1;
    private int lastCompletedGameNumber = -1;

    public List<PokerPlayerHistory> PlayerHistories
    {
        get { return playerHistories; }
    }

    /// <summary>
    /// 세션 시작 또는 플레이어 목록 재구성 시 호출합니다.
    /// 기존 누적 기록은 유지하고 누락된 플레이어 항목만 만듭니다.
    /// </summary>
    public void Initialize(IList<PlayerControl> players)
    {
        playerHistories.RemoveAll(
            delegate (PokerPlayerHistory history)
            {
                return history == null;
            }
        );

        if (players == null)
        {
            return;
        }

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player != null)
            {
                GetOrCreateHistory(player.playerNumber);
            }
        }
    }

    /// <summary>
    /// 새 판 시작 시 각 플레이어의 시작 금액을 저장합니다.
    /// </summary>
    public void BeginHand(
        IList<PlayerControl> players,
        int gameNumber)
    {
        currentGameNumber = gameNumber;
        currentHandRecords.Clear();

        if (players == null)
        {
            return;
        }

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player == null)
            {
                continue;
            }

            GetOrCreateHistory(player.playerNumber);

            PokerAICurrentHandRecord record =
                new PokerAICurrentHandRecord
                {
                    gameNumber = gameNumber,
                    startingMoney = player.CurrentMoney,
                    exchangeCount = -1
                };

            currentHandRecords[player.playerNumber] = record;
        }
    }

    /// <summary>
    /// 실제로 처리된 베팅 행동을 기록합니다.
    /// </summary>
    public void RecordBettingAction(
        PlayerControl player,
        BettingAction action,
        GamePhase phase,
        long callAmountBeforeAction,
        long paidAmount,
        long potBeforeAction,
        long highestBetBeforeAction,
        int gameNumber)
    {
        if (player == null)
        {
            return;
        }

        PokerPlayerHistory history =
            GetOrCreateHistory(player.playerNumber);

        history.bettingActionCount++;
        history.totalPaidByActions += Math.Max(0L, paidAmount);
        history.lastBettingAction = action;
        history.hasLastBettingAction = true;
        history.lastBettingGameNumber = gameNumber;
        history.lastBettingPhase = phase;
        history.lastPaidAmount = Math.Max(0L, paidAmount);
        history.lastCallAmountBeforeAction =
            Math.Max(0L, callAmountBeforeAction);
        history.lastPotBeforeAction =
            Math.Max(0L, potBeforeAction);
        history.lastHighestBetBeforeAction =
            Math.Max(0L, highestBetBeforeAction);

        bool isRaise = IsRaiseAction(action);

        switch (action)
        {
            case BettingAction.Fold:
                history.foldCount++;
                break;

            case BettingAction.Check:
                history.checkCount++;
                break;

            case BettingAction.Call:
                history.callCount++;
                history.totalCallPaid += Math.Max(0L, paidAmount);
                break;

            case BettingAction.AllIn:
                history.allInCount++;
                history.raiseCount++;
                history.totalRaisePaid += Math.Max(0L, paidAmount);
                break;

            default:
                if (isRaise)
                {
                    history.raiseCount++;
                    history.totalRaisePaid += Math.Max(0L, paidAmount);
                }
                break;
        }

        if (isRaise)
        {
            if (phase == GamePhase.FirstBetting)
            {
                history.firstBettingRaiseCount++;
            }
            else if (phase == GamePhase.FinalBetting)
            {
                history.finalBettingRaiseCount++;
            }
        }

        PokerAICurrentHandRecord handRecord =
            GetOrCreateCurrentHandRecord(
                player.playerNumber,
                gameNumber,
                player.MoneyAtGameStart
            );

        if (isRaise)
        {
            handRecord.madeAggressiveAction = true;

            if (phase == GamePhase.FinalBetting)
            {
                handRecord.madeAggressiveActionInFinalBetting = true;
            }
        }

        PokerAIRecentActionRecord recentRecord =
            new PokerAIRecentActionRecord
            {
                gameNumber = gameNumber,
                phase = phase,
                action = action,
                callAmountBeforeAction =
                    Math.Max(0L, callAmountBeforeAction),
                paidAmount = Math.Max(0L, paidAmount),
                potBeforeAction = Math.Max(0L, potBeforeAction),
                highestBetBeforeAction =
                    Math.Max(0L, highestBetBeforeAction)
            };

        history.recentActions.Add(recentRecord);
        TrimRecentActions(history);
    }

    /// <summary>
    /// 공개된 교환 장수를 기록합니다.
    /// </summary>
    public void RecordExchange(
        PlayerControl player,
        int exchangedCardCount,
        int gameNumber)
    {
        if (player == null)
        {
            return;
        }

        exchangedCardCount = Mathf.Clamp(
            exchangedCardCount,
            0,
            5
        );

        PokerPlayerHistory history =
            GetOrCreateHistory(player.playerNumber);

        history.exchangeDecisionCount++;
        history.totalExchangedCards += exchangedCardCount;
        history.lastExchangeCount = exchangedCardCount;

        if (exchangedCardCount == 0)
        {
            history.noExchangeCount++;
        }

        PokerAICurrentHandRecord handRecord =
            GetOrCreateCurrentHandRecord(
                player.playerNumber,
                gameNumber,
                player.MoneyAtGameStart
            );

        handRecord.exchangeCount = exchangedCardCount;
    }

    /// <summary>
    /// 승자 정산이 완료된 뒤 한 판의 결과를 누적합니다.
    /// reachedShowdown이 false이면 상대가 보지 못한 숨은 카드는 분석하지 않습니다.
    /// </summary>
    public void CompleteHand(
        IList<PlayerControl> players,
        int gameNumber,
        bool reachedShowdown)
    {
        if (players == null ||
            lastCompletedGameNumber == gameNumber)
        {
            return;
        }

        lastCompletedGameNumber = gameNumber;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player == null)
            {
                continue;
            }

            PokerPlayerHistory history =
                GetOrCreateHistory(player.playerNumber);

            history.handsObserved++;

            long netChange = player.NetMoneyChangeThisGame;
            history.lastHandNetChange = netChange;
            history.cumulativeNetChange += netChange;

            if (player.IsWinnerThisGame)
            {
                history.wins++;
                history.currentWinStreak++;
                history.currentLossStreak = 0;
            }
            else
            {
                history.currentLossStreak++;
                history.currentWinStreak = 0;
            }

            PokerAICurrentHandRecord handRecord;

            if (!currentHandRecords.TryGetValue(
                    player.playerNumber,
                    out handRecord))
            {
                handRecord = new PokerAICurrentHandRecord
                {
                    gameNumber = gameNumber,
                    startingMoney = player.MoneyAtGameStart
                };
            }

            handRecord.reachedShowdown =
                reachedShowdown && !player.IsFolded;

            // 실제 쇼다운에 참여해 공개된 5장만 분석합니다.
            if (handRecord.reachedShowdown &&
                player.HasValidFiveCardHand())
            {
                PokerHandValue value =
                    PokerHandEvaluator.Evaluate(
                        player.cardNumbers
                    );

                history.showdowns++;
                history.revealedHandCount++;
                history.lastRevealedCategory = value.Category;

                if (player.IsWinnerThisGame)
                {
                    history.showdownWins++;
                }

                if (handRecord.madeAggressiveActionInFinalBetting)
                {
                    history.aggressiveShowdownCount++;

                    if (value.Category <= PokerHandCategory.OnePair)
                    {
                        history.weakAggressiveShowdownCount++;
                    }
                }
                else if (value.Category >= PokerHandCategory.Flush)
                {
                    // 플러시 이상을 가지고 최종 베팅에서 레이즈하지 않았다면
                    // 슬로우플레이 또는 트랩 성향의 표본으로 사용합니다.
                    history.strongPassiveShowdownCount++;
                }
            }
        }

        currentHandRecords.Clear();
        currentGameNumber = -1;
    }

    /// <summary>
    /// AI 판단에 사용할 0~1 정규화 성향값을 반환합니다.
    /// 표본이 적을 때는 과도한 확신을 막기 위해 중립값과 섞습니다.
    /// </summary>
    public PokerAIOpponentRead GetRead(int playerNumber)
    {
        PokerPlayerHistory history =
            GetOrCreateHistory(playerNumber);

        float actionSampleWeight =
            Mathf.Clamp01(history.bettingActionCount / 18f);

        float handSampleWeight =
            Mathf.Clamp01(history.handsObserved / 12f);

        float showdownSampleWeight =
            Mathf.Clamp01(history.showdowns / 8f);

        float rawFoldRate = SafeRatio(
            history.foldCount,
            history.bettingActionCount
        );

        float rawCallRate = SafeRatio(
            history.callCount,
            history.bettingActionCount
        );

        float rawCheckRate = SafeRatio(
            history.checkCount,
            history.bettingActionCount
        );

        float rawRaiseRate = SafeRatio(
            history.raiseCount,
            history.bettingActionCount
        );

        float aggressionDenominator =
            history.raiseCount +
            history.callCount +
            history.checkCount;

        float rawAggression =
            aggressionDenominator > 0f
                ? history.raiseCount /
                  aggressionDenominator
                : 0.33f;

        float rawAllInRate = SafeRatio(
            history.allInCount,
            history.bettingActionCount
        );

        float rawNoExchangeRate = SafeRatio(
            history.noExchangeCount,
            history.exchangeDecisionCount
        );

        float averageExchangeCount =
            history.exchangeDecisionCount > 0
                ? history.totalExchangedCards /
                  (float)history.exchangeDecisionCount
                : 2f;

        float rawShowdownWinRate = SafeRatio(
            history.showdownWins,
            history.showdowns
        );

        float rawOverallWinRate = SafeRatio(
            history.wins,
            history.handsObserved
        );

        float rawBluffLikelihood =
            history.aggressiveShowdownCount > 0
                ? history.weakAggressiveShowdownCount /
                  (float)history.aggressiveShowdownCount
                : 0.25f;

        float rawTrapLikelihood =
            history.revealedHandCount > 0
                ? history.strongPassiveShowdownCount /
                  (float)history.revealedHandCount
                : 0.15f;

        float momentum = Mathf.Clamp(
            (history.currentWinStreak * 0.16f) -
            (history.currentLossStreak * 0.12f),
            -1f,
            1f
        );

        float bankrollTrend = 0f;

        if (history.handsObserved > 0)
        {
            double averageNet =
                history.cumulativeNetChange /
                (double)history.handsObserved;

            double averageRisk =
                history.totalPaidByActions /
                (double)Math.Max(1, history.handsObserved);

            double scale = Math.Max(
                1d,
                averageRisk
            );

            bankrollTrend = Mathf.Clamp(
                (float)(averageNet / scale),
                -1f,
                1f
            );
        }

        return new PokerAIOpponentRead
        {
            playerNumber = playerNumber,
            handsObserved = history.handsObserved,
            foldRate = BlendWithNeutral(
                rawFoldRate,
                0.28f,
                actionSampleWeight
            ),
            callRate = BlendWithNeutral(
                rawCallRate,
                0.34f,
                actionSampleWeight
            ),
            checkRate = BlendWithNeutral(
                rawCheckRate,
                0.24f,
                actionSampleWeight
            ),
            raiseRate = BlendWithNeutral(
                rawRaiseRate,
                0.22f,
                actionSampleWeight
            ),
            aggressionRate = BlendWithNeutral(
                rawAggression,
                0.36f,
                actionSampleWeight
            ),
            allInRate = BlendWithNeutral(
                rawAllInRate,
                0.04f,
                actionSampleWeight
            ),
            noExchangeRate = BlendWithNeutral(
                rawNoExchangeRate,
                0.18f,
                handSampleWeight
            ),
            averageExchangeCount = Mathf.Lerp(
                2f,
                averageExchangeCount,
                handSampleWeight
            ),
            showdownWinRate = BlendWithNeutral(
                rawShowdownWinRate,
                0.28f,
                showdownSampleWeight
            ),
            overallWinRate = BlendWithNeutral(
                rawOverallWinRate,
                0.20f,
                handSampleWeight
            ),
            bluffLikelihood = BlendWithNeutral(
                rawBluffLikelihood,
                0.25f,
                showdownSampleWeight
            ),
            trapLikelihood = BlendWithNeutral(
                rawTrapLikelihood,
                0.15f,
                showdownSampleWeight
            ),
            recentMomentum = momentum,
            bankrollTrend = bankrollTrend,
            lastHandNetChange = history.lastHandNetChange,
            currentWinStreak = history.currentWinStreak,
            currentLossStreak = history.currentLossStreak,
            lastExchangeCount = history.lastExchangeCount,
            lastBettingAction = history.lastBettingAction,
            hasLastBettingAction = history.hasLastBettingAction,
            lastBettingGameNumber = history.lastBettingGameNumber,
            lastBettingPhase = history.lastBettingPhase,
            lastPaidAmount = history.lastPaidAmount,
            lastCallAmountBeforeAction =
                history.lastCallAmountBeforeAction,
            lastPotBeforeAction = history.lastPotBeforeAction,
            lastHighestBetBeforeAction =
                history.lastHighestBetBeforeAction
        };
    }

    public PokerPlayerHistory GetHistory(int playerNumber)
    {
        return GetOrCreateHistory(playerNumber);
    }

    [ContextMenu("Clear AI History")]
    public void ClearHistory()
    {
        playerHistories.Clear();
        currentHandRecords.Clear();
        currentGameNumber = -1;
        lastCompletedGameNumber = -1;
    }

    private PokerPlayerHistory GetOrCreateHistory(
        int playerNumber)
    {
        for (int i = 0; i < playerHistories.Count; i++)
        {
            PokerPlayerHistory history = playerHistories[i];

            if (history != null &&
                history.playerNumber == playerNumber)
            {
                if (history.recentActions == null)
                {
                    history.recentActions =
                        new List<PokerAIRecentActionRecord>();
                }

                return history;
            }
        }

        PokerPlayerHistory created =
            new PokerPlayerHistory
            {
                playerNumber = playerNumber
            };

        playerHistories.Add(created);
        playerHistories.Sort(
            delegate (
                PokerPlayerHistory a,
                PokerPlayerHistory b)
            {
                return a.playerNumber.CompareTo(
                    b.playerNumber
                );
            }
        );

        return created;
    }

    private PokerAICurrentHandRecord
        GetOrCreateCurrentHandRecord(
            int playerNumber,
            int gameNumber,
            long startingMoney)
    {
        PokerAICurrentHandRecord record;

        if (currentHandRecords.TryGetValue(
                playerNumber,
                out record))
        {
            return record;
        }

        record = new PokerAICurrentHandRecord
        {
            gameNumber = gameNumber,
            startingMoney = startingMoney
        };

        currentHandRecords[playerNumber] = record;
        return record;
    }

    private void TrimRecentActions(
        PokerPlayerHistory history)
    {
        if (history == null || history.recentActions == null)
        {
            return;
        }

        int capacity = Math.Max(
            5,
            recentActionCapacityPerPlayer
        );

        while (history.recentActions.Count > capacity)
        {
            history.recentActions.RemoveAt(0);
        }
    }

    private bool IsRaiseAction(BettingAction action)
    {
        return action == BettingAction.Ping ||
               action == BettingAction.Double ||
               action == BettingAction.Quarter ||
               action == BettingAction.Half ||
               action == BettingAction.AllIn ||
               action == BettingAction.Max;
    }

    private float SafeRatio(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0f;
        }

        return Mathf.Clamp01(
            numerator / (float)denominator
        );
    }

    private float BlendWithNeutral(
        float observed,
        float neutral,
        float sampleWeight)
    {
        return Mathf.Clamp01(
            Mathf.Lerp(
                neutral,
                observed,
                Mathf.Clamp01(sampleWeight)
            )
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        recentActionCapacityPerPlayer =
            Mathf.Max(5, recentActionCapacityPerPlayer);
    }
#endif
}