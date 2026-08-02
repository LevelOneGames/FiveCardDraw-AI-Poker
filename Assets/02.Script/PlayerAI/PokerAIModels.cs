using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컴퓨터 플레이어의 기본 성격입니다.
/// 세부 강도는 PlayerControl의 공격성, 패 선별력, 블러프 성향 3개 값으로 조절합니다.
/// </summary>
public enum PokerAIStyle
{
    [InspectorName("보수형")]
    Conservative = 0,

    [InspectorName("공격형")]
    Aggressive = 1,

    [InspectorName("계산형")]
    Calculated = 2,

    [InspectorName("변칙형")]
    Trickster = 3,

    [InspectorName("커스텀")]
    Custom = 4
}

/// <summary>
/// 모든 AI가 공통으로 사용하는 3개 성향값입니다.
/// 0~1 범위로 사용하며 환경설정 Slider와 바로 연결할 수 있습니다.
/// </summary>
[Serializable]
public class PokerAIParameters
{
    public PokerAIStyle style = PokerAIStyle.Conservative;
    public float aggression = 0.25f;
    public float handSelectivity = 0.88f;
    public float bluffTendency = 0.12f;

    public PokerAIParameters Clone()
    {
        return new PokerAIParameters
        {
            style = style,
            aggression = aggression,
            handSelectivity = handSelectivity,
            bluffTendency = bluffTendency
        };
    }
}

/// <summary>
/// AI가 알 수 있는 공개 플레이어 정보입니다.
/// 상대방 카드 번호는 의도적으로 포함하지 않습니다.
/// </summary>
[Serializable]
public class PokerAIPublicPlayerState
{
    public int playerNumber;
    public int seatIndex;
    public bool isHuman;
    public bool isFolded;
    public bool isAllIn;
    public bool hasExchanged;
    public int exchangedCardCount;
    public long currentMoney;
    public long roundBetMoney;
    public long totalBetThisGame;
    public PokerAIOpponentRead historyRead;
}

/// <summary>
/// 현재 사용할 수 있는 베팅 행동과 실제 추가 납부액입니다.
/// </summary>
[Serializable]
public class PokerAIBettingOption
{
    public BettingAction action;
    public long additionalAmount;
    public long targetRoundBet;
    public bool isRaise;
    public float utility;
}

/// <summary>
/// 한 플레이어에 대해 과거 공개 행동으로 추정한 성향입니다.
/// </summary>
[Serializable]
public class PokerAIOpponentRead
{
    public int playerNumber;
    public int handsObserved;
    public float foldRate;
    public float callRate;
    public float checkRate;
    public float raiseRate;
    public float aggressionRate;
    public float allInRate;
    public float noExchangeRate;
    public float averageExchangeCount;
    public float showdownWinRate;
    public float overallWinRate;
    public float bluffLikelihood;
    public float trapLikelihood;
    public float recentMomentum;
    public float bankrollTrend;
    public long lastHandNetChange;
    public int currentWinStreak;
    public int currentLossStreak;
    public int lastExchangeCount;
    public BettingAction lastBettingAction;
    public bool hasLastBettingAction;
    public int lastBettingGameNumber;
    public GamePhase lastBettingPhase;
    public long lastPaidAmount;
    public long lastCallAmountBeforeAction;
    public long lastPotBeforeAction;
    public long lastHighestBetBeforeAction;
}

/// <summary>
/// 베팅 판단에 필요한 모든 공개 정보입니다.
/// </summary>
public class PokerAIBettingContext
{
    public int gameNumber;
    public GamePhase phase;
    public int playerNumber;
    public int seatIndex;
    public int dealerIndex;
    public int smallBlindIndex;
    public int bigBlindIndex;
    public int activeOpponentCount;
    public float positionScore;
    public long totalPot;
    public long currentCallAmount;
    public long currentHighestBet;
    public long ownCurrentMoney;
    public long ownRoundBetMoney;
    public long ownTotalBetThisGame;
    public List<int> ownCards = new List<int>(5);
    public PokerAIParameters parameters = new PokerAIParameters();
    public PokerAIOpponentRead ownHistoryRead;
    public List<PokerAIPublicPlayerState> opponents =
        new List<PokerAIPublicPlayerState>();
    public List<PokerAIBettingOption> availableOptions =
        new List<PokerAIBettingOption>();
    public int monteCarloSamples = 160;
}

/// <summary>
/// 카드 교환 판단에 필요한 정보입니다.
/// </summary>
public class PokerAIExchangeContext
{
    public int gameNumber;
    public int playerNumber;
    public long totalPot;
    public long ownCurrentMoney;
    public int maxExchangeCards;
    public List<int> ownCards = new List<int>(5);
    public PokerAIParameters parameters = new PokerAIParameters();
    public PokerAIOpponentRead ownHistoryRead;
    public List<PokerAIPublicPlayerState> opponents =
        new List<PokerAIPublicPlayerState>();
    public int monteCarloSamplesPerCandidate = 180;
}

/// <summary>
/// 베팅 결정 결과입니다.
/// </summary>
public class PokerAIBettingDecision
{
    public BettingAction action;
    public float estimatedEquity;
    public float potOdds;
    public float confidence;
    public string reason;
    public List<PokerAIBettingOption> evaluatedOptions =
        new List<PokerAIBettingOption>();
}

/// <summary>
/// 교환 결정 결과입니다.
/// </summary>
public class PokerAIExchangeDecision
{
    public List<int> exchangeIndexes = new List<int>();
    public float expectedScore;
    public float confidence;
    public string reason;
}