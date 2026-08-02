using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 파이브 카드 드로우 AI의 실제 판단 엔진입니다.
/// MonoBehaviour가 아니므로 게임 상태를 직접 변경하지 않고,
/// 전달받은 공개 정보만 분석해 행동 결과를 반환합니다.
/// </summary>
public static class PokerAIBrain
{
    private class ExchangeCandidate
    {
        public List<int> indexes = new List<int>();
        public float averageScore;
        public float improvementRate;
        public float premiumFinishRate;
        public float variance;
        public float utility;
    }

    private class ScoredOption
    {
        public PokerAIBettingOption option;
        public float utility;
    }

    /// <summary>
    /// 베팅 행동을 선택합니다.
    /// </summary>
    public static PokerAIBettingDecision DecideBetting(
        PokerAIBettingContext context,
        System.Random random)
    {
        PokerAIBettingDecision result =
            new PokerAIBettingDecision();

        if (context == null ||
            context.ownCards == null ||
            context.ownCards.Count != 5 ||
            context.availableOptions == null ||
            context.availableOptions.Count == 0)
        {
            result.action = BettingAction.Check;
            result.reason = "유효한 베팅 정보가 없어 안전 행동을 선택";
            return result;
        }

        if (random == null)
        {
            random = new System.Random();
        }

        PokerAIParameters parameters =
            context.parameters ?? new PokerAIParameters();

        PokerHandValue currentHand =
            PokerHandEvaluator.Evaluate(context.ownCards);

        float madeHandStrength =
            GetNormalizedHandScore(currentHand);

        float rawEquity = EstimateShowdownEquity(
            context.ownCards,
            Math.Max(1, context.activeOpponentCount),
            Math.Max(40, context.monteCarloSamples),
            random
        );

        float opponentRangePenalty =
            EstimateOpponentRangePenalty(context);

        rawEquity = Mathf.Clamp01(
            rawEquity - opponentRangePenalty
        );

        float futurePotential =
            EstimateDrawPotential(context.ownCards);

        float estimatedEquity;

        if (context.phase == GamePhase.FirstBetting)
        {
            // 첫 베팅에서는 현재 완성패뿐 아니라 교환 후 개선 가능성을 크게 반영합니다.
            estimatedEquity = Mathf.Clamp01(
                (rawEquity * 0.46f) +
                (futurePotential * 0.39f) +
                (madeHandStrength * 0.15f)
            );
        }
        else
        {
            estimatedEquity = Mathf.Clamp01(
                (rawEquity * 0.82f) +
                (madeHandStrength * 0.18f)
            );
        }

        float potOdds =
            context.currentCallAmount > 0L
                ? context.currentCallAmount /
                  (float)Math.Max(
                      1L,
                      context.totalPot +
                      context.currentCallAmount
                  )
                : 0f;

        float averageOpponentFoldRate =
            GetAverageOpponentValue(
                context.opponents,
                delegate (PokerAIOpponentRead read)
                {
                    return read.foldRate;
                },
                0.28f
            );

        float averageOpponentAggression =
            GetAverageOpponentValue(
                context.opponents,
                delegate (PokerAIOpponentRead read)
                {
                    return read.aggressionRate;
                },
                0.36f
            );

        float averageOpponentBluff =
            GetAverageOpponentValue(
                context.opponents,
                delegate (PokerAIOpponentRead read)
                {
                    return read.bluffLikelihood;
                },
                0.25f
            );

        float averageOpponentTrap =
            GetAverageOpponentValue(
                context.opponents,
                delegate (PokerAIOpponentRead read)
                {
                    return read.trapLikelihood;
                },
                0.15f
            );

        float vulnerableStackRatio =
            EstimateVulnerableOpponentStackRatio(
                context.opponents,
                context.totalPot
            );

        float ownStackPressure =
            context.ownCurrentMoney > 0L
                ? context.currentCallAmount /
                  (float)context.ownCurrentMoney
                : 1f;

        float tableMoneyAverage =
            GetAverageTableMoney(context);

        float relativeStack =
            tableMoneyAverage > 0f
                ? context.ownCurrentMoney /
                  tableMoneyAverage
                : 1f;

        float ownMomentum =
            context.ownHistoryRead != null
                ? context.ownHistoryRead.recentMomentum
                : 0f;

        List<ScoredOption> scoredOptions =
            new List<ScoredOption>();

        float potScale = Mathf.Max(
            1f,
            (float)context.totalPot +
            (float)context.currentCallAmount
        );

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

            float utility = EvaluateBettingOption(
                context,
                option,
                estimatedEquity,
                madeHandStrength,
                futurePotential,
                potOdds,
                averageOpponentFoldRate,
                averageOpponentAggression,
                averageOpponentBluff,
                averageOpponentTrap,
                vulnerableStackRatio,
                ownStackPressure,
                relativeStack,
                ownMomentum,
                potScale,
                parameters
            );

            // 같은 정보에서 항상 똑같은 행동만 하지 않도록 아주 작은 인간적 오차를 더합니다.
            float decisionNoise =
                GetDecisionNoiseAmplitude(parameters) *
                NextSignedFloat(random);

            utility += decisionNoise;

            PokerAIBettingOption evaluatedCopy =
                new PokerAIBettingOption
                {
                    action = option.action,
                    additionalAmount = option.additionalAmount,
                    targetRoundBet = option.targetRoundBet,
                    isRaise = option.isRaise,
                    utility = utility
                };

            result.evaluatedOptions.Add(evaluatedCopy);

            scoredOptions.Add(
                new ScoredOption
                {
                    option = evaluatedCopy,
                    utility = utility
                }
            );
        }

        if (scoredOptions.Count == 0)
        {
            result.action = context.currentCallAmount > 0L
                ? BettingAction.Fold
                : BettingAction.Check;
            result.estimatedEquity = estimatedEquity;
            result.potOdds = potOdds;
            result.reason = "가능한 행동이 없어 안전 행동을 선택";
            return result;
        }

        scoredOptions.Sort(
            delegate (ScoredOption a, ScoredOption b)
            {
                return b.utility.CompareTo(a.utility);
            }
        );

        ScoredOption selected = SelectHumanLikeOption(
            scoredOptions,
            parameters,
            random
        );

        float secondBest =
            scoredOptions.Count > 1
                ? scoredOptions[1].utility
                : scoredOptions[0].utility - 0.25f;

        result.action = selected.option.action;
        result.estimatedEquity = estimatedEquity;
        result.potOdds = potOdds;
        result.confidence = Mathf.Clamp01(
            (scoredOptions[0].utility - secondBest) * 1.8f +
            0.35f
        );

        result.reason = CreateBettingReason(
            context,
            currentHand,
            result,
            selected.option,
            averageOpponentFoldRate,
            averageOpponentAggression,
            averageOpponentBluff,
            opponentRangePenalty
        );

        return result;
    }

    /// <summary>
    /// 교환할 카드 인덱스를 선택합니다.
    /// 모든 가능한 0~maxExchangeCards 조합을 평가하되,
    /// 완성 스트레이트 이상은 절대 깨지 않도록 보호합니다.
    /// </summary>
    public static PokerAIExchangeDecision DecideExchange(
        PokerAIExchangeContext context,
        System.Random random)
    {
        PokerAIExchangeDecision result =
            new PokerAIExchangeDecision();

        if (context == null ||
            context.ownCards == null ||
            context.ownCards.Count != 5)
        {
            result.reason = "유효한 카드 5장이 없어 교환하지 않음";
            return result;
        }

        if (random == null)
        {
            random = new System.Random();
        }

        PokerHandValue currentHand =
            PokerHandEvaluator.Evaluate(context.ownCards);

        // 이미 완성된 강한 패는 변칙형도 깨지 않습니다.
        if (currentHand.Category >= PokerHandCategory.Straight)
        {
            result.expectedScore =
                GetNormalizedHandScore(currentHand);
            result.confidence = 1f;
            result.reason =
                PokerHandEvaluator.GetCategoryName(
                    currentHand.Category
                ) + " 완성패 유지";
            return result;
        }

        int maxExchange = Mathf.Clamp(
            context.maxExchangeCards,
            0,
            5
        );

        List<List<int>> combinations =
            CreateExchangeCombinations(maxExchange);

        List<int> remainingDeck =
            BuildRemainingDeck(context.ownCards);

        List<ExchangeCandidate> candidates =
            new List<ExchangeCandidate>();

        float currentScore =
            GetNormalizedHandScore(currentHand);

        for (int i = 0; i < combinations.Count; i++)
        {
            List<int> indexes = combinations[i];

            ExchangeCandidate candidate =
                EvaluateExchangeCandidate(
                    context,
                    currentHand,
                    currentScore,
                    indexes,
                    remainingDeck,
                    random
                );

            candidates.Add(candidate);
        }

        candidates.Sort(
            delegate (
                ExchangeCandidate a,
                ExchangeCandidate b)
            {
                return b.utility.CompareTo(a.utility);
            }
        );

        if (candidates.Count == 0)
        {
            result.reason = "교환 후보가 없어 패스";
            return result;
        }

        ExchangeCandidate selected = candidates[0];

        PokerAIParameters parameters =
            context.parameters ?? new PokerAIParameters();

        // 변칙형은 품질 차이가 아주 작을 때만 두 번째 선택을 섞습니다.
        if (parameters.style == PokerAIStyle.Trickster &&
            candidates.Count > 1)
        {
            float gap =
                candidates[0].utility -
                candidates[1].utility;

            float alternateChance =
                parameters.bluffTendency *
                Mathf.Clamp01(0.18f - gap) *
                0.9f;

            if (NextFloat(random) < alternateChance)
            {
                selected = candidates[1];
            }
        }

        result.exchangeIndexes =
            new List<int>(selected.indexes);
        result.exchangeIndexes.Sort();
        result.expectedScore = selected.averageScore;

        float secondUtility =
            candidates.Count > 1
                ? candidates[1].utility
                : selected.utility - 0.2f;

        result.confidence = Mathf.Clamp01(
            (selected.utility - secondUtility) * 2.4f +
            0.42f
        );

        result.reason = CreateExchangeReason(
            currentHand,
            selected,
            context.ownCards
        );

        return result;
    }

    private static float EvaluateBettingOption(
        PokerAIBettingContext context,
        PokerAIBettingOption option,
        float equity,
        float madeHandStrength,
        float futurePotential,
        float potOdds,
        float averageOpponentFoldRate,
        float averageOpponentAggression,
        float averageOpponentBluff,
        float averageOpponentTrap,
        float vulnerableStackRatio,
        float ownStackPressure,
        float relativeStack,
        float ownMomentum,
        float potScale,
        PokerAIParameters parameters)
    {
        long cost = Math.Max(
            0L,
            option.additionalAmount
        );

        float costRatioToPot =
            cost / Math.Max(1f, potScale);

        float costRatioToStack =
            context.ownCurrentMoney > 0L
                ? cost /
                  (float)context.ownCurrentMoney
                : 1f;

        float ownCommittedRatio =
            context.ownCurrentMoney +
            context.ownTotalBetThisGame > 0L
                ? context.ownTotalBetThisGame /
                  (float)(context.ownCurrentMoney +
                          context.ownTotalBetThisGame)
                : 0f;

        float utility;

        switch (option.action)
        {
            case BettingAction.Fold:
                // 공짜 체크가 가능한데 다이하는 비현실적 행동은 금지합니다.
                if (context.currentCallAmount <= 0L)
                {
                    return -100f;
                }

                utility = 0f;

                // 선택성이 높고 팟오즈보다 승률이 낮으면 손실 회피 가치를 높입니다.
                utility +=
                    Mathf.Max(0f, potOdds - equity) *
                    (0.7f + parameters.handSelectivity * 0.9f);

                utility +=
                    ownStackPressure *
                    parameters.handSelectivity *
                    0.24f;

                if (parameters.style == PokerAIStyle.Conservative)
                {
                    utility += 0.07f;
                }

                break;

            case BettingAction.Check:
                utility =
                    (equity * 0.42f) +
                    (futurePotential *
                     (context.phase == GamePhase.FirstBetting
                         ? 0.18f
                         : 0.03f));

                // 강한 패를 숨기는 슬로우플레이 가능성입니다.
                if (madeHandStrength > 0.72f)
                {
                    utility +=
                        parameters.bluffTendency *
                        GetTrapStyleMultiplier(parameters.style) *
                        0.17f;
                }

                break;

            case BettingAction.Call:
                {
                    float callCost = Mathf.Max(
                        0f,
                        (float)option.additionalAmount
                    );

                    // 현재 판단 시점 기준 증분 EV입니다.
                    // 이기면 기존 팟을 얻고, 지면 새로 콜한 금액만 잃습니다.
                    // 자기 콜 금액은 승리 시 되돌아오는 돈이므로 이익에 중복 계산하지 않습니다.
                    float expectedValue =
                        (equity * context.totalPot) -
                        ((1f - equity) * callCost);

                    utility = expectedValue / potScale;

                    // 상대가 자주 블러프하면 중간 패 콜다운을 늘립니다.
                    utility +=
                        averageOpponentBluff *
                        averageOpponentAggression *
                        (0.08f + (1f - parameters.handSelectivity) * 0.08f);

                    // 트랩 성향이 높은 상대의 큰 베팅에는 경계합니다.
                    utility -=
                        averageOpponentTrap *
                        costRatioToPot *
                        0.14f;

                    if (parameters.style == PokerAIStyle.Conservative)
                    {
                        utility -=
                            Mathf.Max(0f, potOdds - equity) * 0.25f;
                    }
                    else if (parameters.style == PokerAIStyle.Aggressive)
                    {
                        utility += 0.025f;
                    }

                    break;
                }

            default:
                {
                    float pressure = Mathf.Clamp01(
                        costRatioToPot /
                        (0.45f + costRatioToPot)
                    );

                    float foldEquity = Mathf.Clamp01(
                        averageOpponentFoldRate *
                        (0.55f + pressure * 0.85f) *
                        (0.72f + parameters.aggression * 0.42f)
                    );

                    // 여러 명을 상대할수록 전원이 폴드할 확률은 감소합니다.
                    if (context.activeOpponentCount > 1)
                    {
                        foldEquity *= Mathf.Pow(
                            0.82f,
                            context.activeOpponentCount - 1
                        );
                    }

                    foldEquity +=
                        vulnerableStackRatio *
                        pressure *
                        0.10f;

                    foldEquity = Mathf.Clamp01(foldEquity);

                    // 큰 레이즈를 콜하는 상대 범위는 강해지므로 실제 승률을 조금 낮춥니다.
                    float calledEquity = Mathf.Clamp01(
                        equity -
                        (0.025f + pressure * 0.09f) -
                        (averageOpponentTrap * 0.035f)
                    );

                    float likelyCallerContribution =
                        cost *
                        Mathf.Clamp(
                            context.activeOpponentCount *
                            (0.42f - averageOpponentFoldRate * 0.22f),
                            0.15f,
                            1.2f
                        );

                    // 레이즈가 콜을 받았을 때의 증분 EV입니다.
                    // 자기 레이즈 금액은 승리 시 반환되는 원금이므로
                    // 기존 팟과 상대가 새로 낸 금액만 승리 이익으로 계산합니다.
                    float calledWinProfit =
                        context.totalPot +
                        likelyCallerContribution;

                    float calledEV =
                        (calledEquity * calledWinProfit) -
                        ((1f - calledEquity) * cost);

                    float foldEV = context.totalPot;

                    utility =
                        ((foldEquity * foldEV) +
                         ((1f - foldEquity) * calledEV)) /
                        potScale;

                    // 공통 3개 성향값 반영
                    utility +=
                        (parameters.aggression - 0.5f) *
                        (0.24f + pressure * 0.16f);

                    // 늦은 포지션에서는 앞선 플레이를 더 많이 본 뒤 압박할 수 있습니다.
                    utility +=
                        (context.positionScore - 0.5f) *
                        (0.055f + parameters.aggression * 0.035f);

                    utility -=
                        parameters.handSelectivity *
                        Mathf.Max(0f, 0.53f - equity) *
                        (0.32f + pressure * 0.24f);

                    bool isBluffCandidate =
                        equity < 0.46f &&
                        foldEquity > 0.24f;

                    if (isBluffCandidate)
                    {
                        utility +=
                            parameters.bluffTendency *
                            foldEquity *
                            (0.20f + pressure * 0.20f);
                    }

                    // 강한 패의 가치 베팅
                    utility +=
                        Mathf.Max(0f, equity - 0.58f) *
                        (0.25f + pressure * 0.12f);

                    // 큰 금액을 잃을 위험
                    float riskPenalty =
                        costRatioToStack *
                        (0.12f + parameters.handSelectivity * 0.24f);

                    if (relativeStack < 0.65f)
                    {
                        riskPenalty *= 1.18f;
                    }

                    utility -= riskPenalty;

                    ApplyStyleSpecificRaiseUtility(
                        ref utility,
                        option,
                        context,
                        parameters,
                        equity,
                        madeHandStrength,
                        foldEquity,
                        pressure,
                        costRatioToStack,
                        ownMomentum
                    );

                    // 올인/맥스는 일반 레이즈보다 훨씬 엄격하게 제한합니다.
                    if (option.action == BettingAction.AllIn ||
                        option.action == BettingAction.Max)
                    {
                        float requiredEquity =
                            GetLargeBetRequiredEquity(parameters.style);

                        if (equity < requiredEquity)
                        {
                            float bluffPermission =
                                parameters.bluffTendency *
                                parameters.aggression *
                                foldEquity;

                            utility -=
                                (requiredEquity - equity) *
                                (0.95f - bluffPermission * 0.52f);
                        }

                        utility -=
                            Mathf.Max(0f, costRatioToStack - 0.65f) *
                            0.22f;
                    }

                    break;
                }
        }

        // 실제 사람처럼 이미 많이 투자한 판에서 약간 더 버티는 성향을 섞되,
        // 계산형은 매몰비용을 거의 무시합니다.
        if (parameters.style == PokerAIStyle.Aggressive ||
            parameters.style == PokerAIStyle.Trickster)
        {
            if (option.action == BettingAction.Call || option.isRaise)
            {
                utility += ownCommittedRatio * 0.025f;
            }
        }

        // 계산형은 팟오즈와 실제 기대값을 가장 엄격히 따릅니다.
        if (parameters.style == PokerAIStyle.Calculated)
        {
            if (option.action == BettingAction.Call &&
                context.currentCallAmount > 0L)
            {
                utility +=
                    (equity - potOdds) * 0.16f;
            }

            if (option.isRaise)
            {
                utility +=
                    Mathf.Max(0f, equity - 0.5f) * 0.05f;
            }
        }

        return utility;
    }

    private static void ApplyStyleSpecificRaiseUtility(
        ref float utility,
        PokerAIBettingOption option,
        PokerAIBettingContext context,
        PokerAIParameters parameters,
        float equity,
        float madeHandStrength,
        float foldEquity,
        float pressure,
        float costRatioToStack,
        float ownMomentum)
    {
        switch (parameters.style)
        {
            case PokerAIStyle.Conservative:
                utility -=
                    Mathf.Max(0f, 0.66f - equity) *
                    (0.18f + pressure * 0.16f);

                if (equity > 0.72f)
                {
                    utility += 0.09f;
                }
                break;

            case PokerAIStyle.Aggressive:
                utility +=
                    0.08f +
                    parameters.aggression * 0.11f;

                if (pressure < 0.72f)
                {
                    utility += 0.045f;
                }

                if (ownMomentum < -0.2f)
                {
                    // 공격형은 연패 시 약간 더 거칠어지는 틸트 성향이 있습니다.
                    utility +=
                        Mathf.Abs(ownMomentum) * 0.05f;
                }
                break;

            case PokerAIStyle.Calculated:
                // 계산형은 별도 감정 보정이 거의 없습니다.
                utility +=
                    Mathf.Max(0f, equity - 0.58f) * 0.04f;
                break;

            case PokerAIStyle.Trickster:
                if (equity < 0.42f &&
                    foldEquity > 0.30f)
                {
                    utility +=
                        parameters.bluffTendency *
                        (0.09f + pressure * 0.08f);
                }

                // 강한 패에서 너무 큰 베팅 대신 중간 크기를 선호해 함정을 팝니다.
                if (madeHandStrength > 0.82f &&
                    (option.action == BettingAction.AllIn ||
                     option.action == BettingAction.Max))
                {
                    utility -=
                        parameters.bluffTendency * 0.08f;
                }

                if (costRatioToStack < 0.45f)
                {
                    utility += 0.025f;
                }
                break;
        }
    }

    private static ScoredOption SelectHumanLikeOption(
        List<ScoredOption> options,
        PokerAIParameters parameters,
        System.Random random)
    {
        if (options == null || options.Count == 0)
        {
            return null;
        }

        if (options.Count == 1)
        {
            return options[0];
        }

        float temperature;

        switch (parameters.style)
        {
            case PokerAIStyle.Conservative:
                temperature = 0.055f;
                break;

            case PokerAIStyle.Aggressive:
                temperature = 0.075f;
                break;

            case PokerAIStyle.Calculated:
                temperature = 0.030f;
                break;

            case PokerAIStyle.Trickster:
                temperature = 0.125f;
                break;

            default:
                temperature = 0.06f;
                break;
        }

        temperature +=
            parameters.bluffTendency * 0.025f;

        float maxUtility = options[0].utility;
        float totalWeight = 0f;
        float[] weights = new float[options.Count];

        for (int i = 0; i < options.Count; i++)
        {
            float delta =
                options[i].utility - maxUtility;

            if (delta < -0.55f)
            {
                weights[i] = 0f;
                continue;
            }

            float exponent = Mathf.Clamp(
                delta / Math.Max(0.01f, temperature),
                -14f,
                0f
            );

            float weight = Mathf.Exp(exponent);
            weights[i] = weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
        {
            return options[0];
        }

        float roll = NextFloat(random) * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < options.Count; i++)
        {
            cumulative += weights[i];

            if (roll <= cumulative)
            {
                return options[i];
            }
        }

        return options[0];
    }

    private static float EstimateShowdownEquity(
        IList<int> ownCards,
        int opponentCount,
        int samples,
        System.Random random)
    {
        if (ownCards == null || ownCards.Count != 5)
        {
            return 0f;
        }

        PokerHandValue ownValue =
            PokerHandEvaluator.Evaluate(ownCards);

        List<int> availableDeck =
            BuildRemainingDeck(ownCards);

        opponentCount = Mathf.Clamp(
            opponentCount,
            1,
            4
        );

        int requiredCards = opponentCount * 5;

        if (availableDeck.Count < requiredCards)
        {
            return GetNormalizedHandScore(ownValue);
        }

        float totalShare = 0f;
        List<int> shuffled =
            new List<int>(availableDeck.Count);

        for (int sample = 0; sample < samples; sample++)
        {
            shuffled.Clear();
            shuffled.AddRange(availableDeck);

            PartialShuffle(
                shuffled,
                requiredCards,
                random
            );

            bool lost = false;
            int tiedOpponents = 0;
            int cursor = 0;

            for (int opponent = 0;
                 opponent < opponentCount;
                 opponent++)
            {
                List<int> opponentCards =
                    new List<int>(5);

                for (int c = 0; c < 5; c++)
                {
                    opponentCards.Add(shuffled[cursor++]);
                }

                PokerHandValue opponentValue =
                    PokerHandEvaluator.Evaluate(
                        opponentCards
                    );

                int compare = ownValue.CompareTo(opponentValue);

                if (compare < 0)
                {
                    lost = true;
                    break;
                }

                if (compare == 0)
                {
                    tiedOpponents++;
                }
            }

            if (!lost)
            {
                totalShare +=
                    1f / (1f + tiedOpponents);
            }
        }

        return Mathf.Clamp01(
            totalShare / Math.Max(1, samples)
        );
    }

    private static float EstimateOpponentRangePenalty(
        PokerAIBettingContext context)
    {
        List<PokerAIPublicPlayerState> opponents =
            context != null ? context.opponents : null;

        if (opponents == null || opponents.Count == 0)
        {
            return 0f;
        }

        float total = 0f;
        int count = 0;

        for (int i = 0; i < opponents.Count; i++)
        {
            PokerAIPublicPlayerState opponent = opponents[i];

            if (opponent == null || opponent.isFolded)
            {
                continue;
            }

            float signal = 0f;

            if (opponent.hasExchanged)
            {
                switch (opponent.exchangedCardCount)
                {
                    case 0:
                        signal += 0.095f;
                        break;
                    case 1:
                        signal += 0.050f;
                        break;
                    case 2:
                        signal += 0.025f;
                        break;
                    case 3:
                        signal -= 0.012f;
                        break;
                    default:
                        signal -= 0.020f;
                        break;
                }
            }

            long visibleStack =
                Math.Max(
                    1L,
                    opponent.currentMoney +
                    opponent.roundBetMoney
                );

            float visibleCommitment =
                opponent.roundBetMoney /
                (float)visibleStack;

            signal += visibleCommitment * 0.025f;

            PokerAIOpponentRead read = opponent.historyRead;

            if (read != null)
            {
                signal +=
                    (read.showdownWinRate - 0.28f) * 0.07f;

                signal +=
                    read.trapLikelihood * 0.035f;

                signal -=
                    read.bluffLikelihood *
                    read.aggressionRate *
                    0.025f;

                signal +=
                    read.recentMomentum * 0.012f;

                signal +=
                    read.bankrollTrend * 0.008f;

                // 같은 판에서 이미 공개된 최근 행동은 누적 평균보다
                // 현재 손패 범위를 더 직접적으로 보여 주므로 별도로 반영합니다.
                if (context != null &&
                    read.hasLastBettingAction &&
                    read.lastBettingGameNumber == context.gameNumber)
                {
                    float actionSignal =
                        GetCurrentActionRangeSignal(
                            read.lastBettingAction
                        );

                    float amountPressure =
                        read.lastPotBeforeAction > 0L
                            ? read.lastPaidAmount /
                              (float)read.lastPotBeforeAction
                            : 0f;

                    actionSignal +=
                        Mathf.Clamp01(amountPressure) * 0.022f;

                    // 평소 블러프가 잦은 상대의 공격 행동은
                    // 강한 패 신호를 덜 신뢰합니다.
                    if (actionSignal > 0f)
                    {
                        actionSignal *=
                            1f -
                            read.bluffLikelihood *
                            read.aggressionRate *
                            0.48f;
                    }
                    else if (actionSignal < 0f)
                    {
                        // 평소 강한 패를 숨기는 상대의 체크는
                        // 약한 패 신호로 과신하지 않습니다.
                        actionSignal *=
                            1f -
                            read.trapLikelihood * 0.70f;
                    }

                    // 같은 베팅 페이즈의 행동을 가장 강하게,
                    // 첫 베팅에서 본 행동은 최종 베팅에서도 약하게 유지합니다.
                    float phaseWeight =
                        read.lastBettingPhase == context.phase
                            ? 1f
                            : 0.55f;

                    signal += actionSignal * phaseWeight;
                }
            }

            total += signal;
            count++;
        }

        if (count <= 0)
        {
            return 0f;
        }

        return Mathf.Clamp(
            total / count,
            -0.04f,
            0.12f
        );
    }

    private static float GetCurrentActionRangeSignal(
        BettingAction action)
    {
        switch (action)
        {
            case BettingAction.Check:
                return -0.008f;
            case BettingAction.Call:
                return 0.010f;
            case BettingAction.Ping:
                return 0.018f;
            case BettingAction.Double:
                return 0.026f;
            case BettingAction.Quarter:
                return 0.032f;
            case BettingAction.Half:
                return 0.044f;
            case BettingAction.Max:
                return 0.056f;
            case BettingAction.AllIn:
                return 0.064f;
            default:
                return 0f;
        }
    }

    private static float EstimateDrawPotential(
        IList<int> cards)
    {
        PokerHandValue value =
            PokerHandEvaluator.Evaluate(cards);

        switch (value.Category)
        {
            case PokerHandCategory.StraightFlush:
                return 0.995f;
            case PokerHandCategory.FourOfAKind:
                return 0.975f;
            case PokerHandCategory.FullHouse:
                return 0.930f;
            case PokerHandCategory.Flush:
                return 0.855f;
            case PokerHandCategory.Straight:
                return 0.825f;
            case PokerHandCategory.ThreeOfAKind:
                return 0.720f;
            case PokerHandCategory.TwoPair:
                return 0.655f;
            case PokerHandCategory.OnePair:
                return 0.475f +
                       GetPairRankBonus(value) * 0.06f;
        }

        float score = 0.22f;

        if (HasFourCardFlushDraw(cards))
        {
            score = Math.Max(score, 0.50f);
        }

        if (HasFourCardStraightDraw(cards))
        {
            score = Math.Max(score, 0.47f);
        }

        int highCard =
            value.TieBreakers != null &&
            value.TieBreakers.Count > 0
                ? value.TieBreakers[0]
                : 2;

        score += Mathf.InverseLerp(10f, 14f, highCard) * 0.08f;

        return Mathf.Clamp01(score);
    }

    private static ExchangeCandidate EvaluateExchangeCandidate(
        PokerAIExchangeContext context,
        PokerHandValue currentHand,
        float currentScore,
        List<int> indexes,
        List<int> remainingDeck,
        System.Random random)
    {
        ExchangeCandidate candidate =
            new ExchangeCandidate
            {
                indexes = new List<int>(indexes)
            };

        int drawCount = indexes.Count;

        if (drawCount == 0)
        {
            candidate.averageScore = currentScore;
            candidate.improvementRate = 0f;
            candidate.premiumFinishRate =
                currentHand.Category >= PokerHandCategory.Straight
                    ? 1f
                    : 0f;
            candidate.variance = 0f;
            candidate.utility = currentScore;

            candidate.utility += GetMadeHandPreservationBonus(
                currentHand,
                indexes,
                context.ownCards
            );

            return candidate;
        }

        int samples = Math.Max(
            60,
            context.monteCarloSamplesPerCandidate
        );

        float scoreSum = 0f;
        float scoreSquaredSum = 0f;
        int improvementCount = 0;
        int premiumFinishCount = 0;

        List<int> shuffled =
            new List<int>(remainingDeck.Count);

        List<int> simulatedHand =
            new List<int>(context.ownCards);

        for (int sample = 0; sample < samples; sample++)
        {
            shuffled.Clear();
            shuffled.AddRange(remainingDeck);

            PartialShuffle(
                shuffled,
                drawCount,
                random
            );

            simulatedHand.Clear();
            simulatedHand.AddRange(context.ownCards);

            for (int d = 0; d < drawCount; d++)
            {
                simulatedHand[indexes[d]] = shuffled[d];
            }

            PokerHandValue finalValue =
                PokerHandEvaluator.Evaluate(simulatedHand);

            float score =
                GetNormalizedHandScore(finalValue);

            scoreSum += score;
            scoreSquaredSum += score * score;

            if (finalValue.CompareTo(currentHand) > 0)
            {
                improvementCount++;
            }

            if (finalValue.Category >= PokerHandCategory.Straight)
            {
                premiumFinishCount++;
            }
        }

        candidate.averageScore =
            scoreSum / samples;

        candidate.improvementRate =
            improvementCount / (float)samples;

        candidate.premiumFinishRate =
            premiumFinishCount / (float)samples;

        float meanSquare =
            scoreSquaredSum / samples;

        candidate.variance = Mathf.Max(
            0f,
            meanSquare -
            candidate.averageScore *
            candidate.averageScore
        );

        PokerAIParameters parameters =
            context.parameters ?? new PokerAIParameters();

        candidate.utility = candidate.averageScore;
        candidate.utility += candidate.improvementRate * 0.075f;
        candidate.utility += candidate.premiumFinishRate * 0.08f;

        candidate.utility += GetMadeHandPreservationBonus(
            currentHand,
            indexes,
            context.ownCards
        );

        candidate.utility += GetDrawStructureBonus(
            context.ownCards,
            indexes
        );

        switch (parameters.style)
        {
            case PokerAIStyle.Conservative:
                candidate.utility -=
                    candidate.variance *
                    (0.38f + parameters.handSelectivity * 0.34f);

                candidate.utility -=
                    drawCount * 0.004f *
                    parameters.handSelectivity;
                break;

            case PokerAIStyle.Aggressive:
                candidate.utility +=
                    candidate.premiumFinishRate *
                    (0.04f + parameters.aggression * 0.04f);
                break;

            case PokerAIStyle.Calculated:
                candidate.utility +=
                    candidate.averageScore * 0.025f;
                candidate.utility -=
                    candidate.variance * 0.08f;
                break;

            case PokerAIStyle.Trickster:
                candidate.utility +=
                    candidate.variance *
                    parameters.bluffTendency *
                    0.10f;
                break;
        }

        // 높은 선별력은 이미 만들어진 조합을 함부로 깨지 않게 합니다.
        candidate.utility +=
            GetSelectiveRetentionBonus(
                currentHand,
                indexes,
                context.ownCards,
                parameters.handSelectivity
            );

        return candidate;
    }

    private static float GetMadeHandPreservationBonus(
        PokerHandValue hand,
        List<int> exchangeIndexes,
        IList<int> cards)
    {
        if (hand == null)
        {
            return 0f;
        }

        if (hand.Category == PokerHandCategory.ThreeOfAKind)
        {
            int tripRank = hand.TieBreakers[0];
            bool keepsTrips = KeepsAllCardsOfRank(
                cards,
                exchangeIndexes,
                tripRank
            );

            return keepsTrips && exchangeIndexes.Count == 2
                ? 0.13f
                : keepsTrips
                    ? 0.05f
                    : -0.30f;
        }

        if (hand.Category == PokerHandCategory.TwoPair)
        {
            int highPair = hand.TieBreakers[0];
            int lowPair = hand.TieBreakers[1];

            bool keepsBoth =
                KeepsAllCardsOfRank(
                    cards,
                    exchangeIndexes,
                    highPair
                ) &&
                KeepsAllCardsOfRank(
                    cards,
                    exchangeIndexes,
                    lowPair
                );

            return keepsBoth && exchangeIndexes.Count == 1
                ? 0.15f
                : keepsBoth
                    ? 0.04f
                    : -0.32f;
        }

        if (hand.Category == PokerHandCategory.OnePair)
        {
            int pairRank = hand.TieBreakers[0];
            bool keepsPair = KeepsAllCardsOfRank(
                cards,
                exchangeIndexes,
                pairRank
            );

            return keepsPair && exchangeIndexes.Count == 3
                ? 0.12f
                : keepsPair
                    ? 0.045f
                    : -0.27f;
        }

        return 0f;
    }

    private static float GetSelectiveRetentionBonus(
        PokerHandValue hand,
        List<int> exchangeIndexes,
        IList<int> cards,
        float selectivity)
    {
        if (hand == null || exchangeIndexes == null)
        {
            return 0f;
        }

        float bonus = 0f;

        for (int i = 0; i < cards.Count; i++)
        {
            if (exchangeIndexes.Contains(i))
            {
                continue;
            }

            int rank =
                (int)CardUtility.GetRank(cards[i]) + 2;

            if (rank >= 12)
            {
                bonus += 0.006f * selectivity;
            }
        }

        return bonus;
    }

    private static float GetDrawStructureBonus(
        IList<int> cards,
        List<int> exchangeIndexes)
    {
        List<int> keptCards = new List<int>();

        for (int i = 0; i < cards.Count; i++)
        {
            if (!exchangeIndexes.Contains(i))
            {
                keptCards.Add(cards[i]);
            }
        }

        float bonus = 0f;

        if (keptCards.Count == 4 &&
            IsSameSuit(keptCards))
        {
            bonus += 0.085f;
        }

        if (keptCards.Count == 4 &&
            IsFourCardStraightStructure(keptCards))
        {
            bonus += 0.072f;
        }

        if (keptCards.Count == 3 &&
            IsThreeCardStraightFlushStructure(keptCards))
        {
            bonus += 0.020f;
        }

        return bonus;
    }

    private static List<List<int>> CreateExchangeCombinations(
        int maxExchange)
    {
        List<List<int>> result =
            new List<List<int>>();

        result.Add(new List<int>());

        for (int count = 1;
             count <= maxExchange;
             count++)
        {
            BuildCombinationsRecursive(
                0,
                count,
                new List<int>(),
                result
            );
        }

        return result;
    }

    private static void BuildCombinationsRecursive(
        int startIndex,
        int remaining,
        List<int> current,
        List<List<int>> result)
    {
        if (remaining <= 0)
        {
            result.Add(new List<int>(current));
            return;
        }

        for (int index = startIndex;
             index <= 5 - remaining;
             index++)
        {
            current.Add(index);

            BuildCombinationsRecursive(
                index + 1,
                remaining - 1,
                current,
                result
            );

            current.RemoveAt(current.Count - 1);
        }
    }

    private static List<int> BuildRemainingDeck(
        IList<int> excludedCards)
    {
        HashSet<int> excluded = new HashSet<int>();

        if (excludedCards != null)
        {
            for (int i = 0; i < excludedCards.Count; i++)
            {
                if (CardUtility.IsValidCardNumber(
                        excludedCards[i]))
                {
                    excluded.Add(excludedCards[i]);
                }
            }
        }

        List<int> deck =
            new List<int>(CardUtility.TotalCardCount);

        for (int card = 0;
             card < CardUtility.TotalCardCount;
             card++)
        {
            if (!excluded.Contains(card))
            {
                deck.Add(card);
            }
        }

        return deck;
    }

    private static void PartialShuffle(
        List<int> list,
        int count,
        System.Random random)
    {
        count = Math.Min(count, list.Count);

        for (int i = 0; i < count; i++)
        {
            int swapIndex = random.Next(i, list.Count);

            int temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    /// <summary>
    /// 족보와 타이브레이커를 0~1 점수로 정규화합니다.
    /// 카테고리 차이가 킥커 차이보다 항상 크게 유지됩니다.
    /// </summary>
    public static float GetNormalizedHandScore(
        PokerHandValue hand)
    {
        if (hand == null)
        {
            return 0f;
        }

        float categoryBase =
            ((int)hand.Category) / 8f;

        float tieScore = 0f;
        float weight = 0.55f;

        if (hand.TieBreakers != null)
        {
            for (int i = 0;
                 i < hand.TieBreakers.Count;
                 i++)
            {
                float normalizedRank = Mathf.InverseLerp(
                    2f,
                    14f,
                    hand.TieBreakers[i]
                );

                tieScore += normalizedRank * weight;
                weight *= 0.42f;
            }
        }

        tieScore = Mathf.Clamp01(tieScore);

        // 한 카테고리 내부에서만 최대 약 0.10의 차이를 만듭니다.
        return Mathf.Clamp01(
            categoryBase * 0.90f +
            tieScore * 0.10f
        );
    }

    private static float GetPairRankBonus(
        PokerHandValue value)
    {
        if (value == null ||
            value.TieBreakers == null ||
            value.TieBreakers.Count == 0)
        {
            return 0f;
        }

        return Mathf.InverseLerp(
            2f,
            14f,
            value.TieBreakers[0]
        );
    }

    private static bool HasFourCardFlushDraw(
        IList<int> cards)
    {
        int[] suitCounts = new int[4];

        for (int i = 0; i < cards.Count; i++)
        {
            suitCounts[(int)CardUtility.GetSuit(cards[i])]++;
        }

        for (int i = 0; i < suitCounts.Length; i++)
        {
            if (suitCounts[i] >= 4)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFourCardStraightDraw(
        IList<int> cards)
    {
        List<int> ranks = GetUniqueAceLowRanks(cards);

        for (int start = 1; start <= 10; start++)
        {
            int count = 0;

            for (int rank = start;
                 rank < start + 5;
                 rank++)
            {
                if (ranks.Contains(rank))
                {
                    count++;
                }
            }

            if (count >= 4)
            {
                return true;
            }
        }

        return false;
    }

    private static List<int> GetUniqueAceLowRanks(
        IList<int> cards)
    {
        List<int> ranks = new List<int>();

        for (int i = 0; i < cards.Count; i++)
        {
            int rank =
                (int)CardUtility.GetRank(cards[i]) + 2;

            if (!ranks.Contains(rank))
            {
                ranks.Add(rank);
            }

            if (rank == 14 && !ranks.Contains(1))
            {
                ranks.Add(1);
            }
        }

        return ranks;
    }

    private static bool KeepsAllCardsOfRank(
        IList<int> cards,
        List<int> exchangeIndexes,
        int targetRank)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            int rank =
                (int)CardUtility.GetRank(cards[i]) + 2;

            if (rank == targetRank &&
                exchangeIndexes.Contains(i))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSameSuit(IList<int> cards)
    {
        if (cards == null || cards.Count == 0)
        {
            return false;
        }

        CardSuit suit = CardUtility.GetSuit(cards[0]);

        for (int i = 1; i < cards.Count; i++)
        {
            if (CardUtility.GetSuit(cards[i]) != suit)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFourCardStraightStructure(
        IList<int> cards)
    {
        if (cards == null || cards.Count != 4)
        {
            return false;
        }

        List<int> ranks = GetUniqueAceLowRanks(cards);

        for (int start = 1; start <= 10; start++)
        {
            bool allContained = true;

            for (int i = 0; i < cards.Count; i++)
            {
                int rank =
                    (int)CardUtility.GetRank(cards[i]) + 2;

                bool fits =
                    rank >= start &&
                    rank <= start + 4;

                if (rank == 14 && start == 1)
                {
                    fits = true;
                }

                if (!fits)
                {
                    allContained = false;
                    break;
                }
            }

            if (allContained && ranks.Count >= 4)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsThreeCardStraightFlushStructure(
        IList<int> cards)
    {
        if (cards == null || cards.Count != 3 ||
            !IsSameSuit(cards))
        {
            return false;
        }

        List<int> ranks = GetUniqueAceLowRanks(cards);
        ranks.Sort();

        int min = ranks[0];
        int max = ranks[ranks.Count - 1];

        return max - min <= 4;
    }

    private static float GetAverageOpponentValue(
        List<PokerAIPublicPlayerState> opponents,
        Func<PokerAIOpponentRead, float> selector,
        float neutral)
    {
        if (opponents == null ||
            opponents.Count == 0 ||
            selector == null)
        {
            return neutral;
        }

        float total = 0f;
        int count = 0;

        for (int i = 0; i < opponents.Count; i++)
        {
            PokerAIPublicPlayerState opponent = opponents[i];

            if (opponent == null ||
                opponent.isFolded ||
                opponent.historyRead == null)
            {
                continue;
            }

            total += selector(opponent.historyRead);
            count++;
        }

        return count > 0
            ? Mathf.Clamp01(total / count)
            : neutral;
    }

    private static float EstimateVulnerableOpponentStackRatio(
        List<PokerAIPublicPlayerState> opponents,
        long pot)
    {
        if (opponents == null || opponents.Count == 0)
        {
            return 0f;
        }

        int vulnerable = 0;
        int active = 0;

        for (int i = 0; i < opponents.Count; i++)
        {
            PokerAIPublicPlayerState opponent = opponents[i];

            if (opponent == null || opponent.isFolded)
            {
                continue;
            }

            active++;

            if (!opponent.isAllIn &&
                opponent.currentMoney < Math.Max(1L, pot))
            {
                vulnerable++;
            }
        }

        return active > 0
            ? vulnerable / (float)active
            : 0f;
    }

    private static float GetAverageTableMoney(
        PokerAIBettingContext context)
    {
        double total = context.ownCurrentMoney;
        int count = 1;

        for (int i = 0; i < context.opponents.Count; i++)
        {
            PokerAIPublicPlayerState opponent =
                context.opponents[i];

            if (opponent == null)
            {
                continue;
            }

            total += opponent.currentMoney;
            count++;
        }

        return count > 0
            ? (float)(total / count)
            : context.ownCurrentMoney;
    }

    private static float GetDecisionNoiseAmplitude(
        PokerAIParameters parameters)
    {
        float baseNoise;

        switch (parameters.style)
        {
            case PokerAIStyle.Conservative:
                baseNoise = 0.018f;
                break;
            case PokerAIStyle.Aggressive:
                baseNoise = 0.028f;
                break;
            case PokerAIStyle.Calculated:
                baseNoise = 0.010f;
                break;
            case PokerAIStyle.Trickster:
                baseNoise = 0.052f;
                break;
            default:
                baseNoise = 0.02f;
                break;
        }

        return baseNoise +
               parameters.bluffTendency * 0.012f;
    }

    private static float GetTrapStyleMultiplier(
        PokerAIStyle style)
    {
        switch (style)
        {
            case PokerAIStyle.Conservative:
                return 0.45f;
            case PokerAIStyle.Aggressive:
                return 0.35f;
            case PokerAIStyle.Calculated:
                return 0.60f;
            case PokerAIStyle.Trickster:
                return 1.15f;
            default:
                return 0.5f;
        }
    }

    private static float GetLargeBetRequiredEquity(
        PokerAIStyle style)
    {
        switch (style)
        {
            case PokerAIStyle.Conservative:
                return 0.76f;
            case PokerAIStyle.Aggressive:
                return 0.61f;
            case PokerAIStyle.Calculated:
                return 0.68f;
            case PokerAIStyle.Trickster:
                return 0.58f;
            default:
                return 0.68f;
        }
    }

    private static string CreateBettingReason(
        PokerAIBettingContext context,
        PokerHandValue hand,
        PokerAIBettingDecision decision,
        PokerAIBettingOption selected,
        float averageFoldRate,
        float averageAggression,
        float averageBluff,
        float rangePenalty)
    {
        string handName =
            PokerHandEvaluator.GetCategoryName(hand.Category);

        return
            "패=" + handName +
            ", 추정승률=" +
            Mathf.RoundToInt(decision.estimatedEquity * 100f) + "%" +
            ", 팟오즈=" +
            Mathf.RoundToInt(decision.potOdds * 100f) + "%" +
            ", 상대폴드=" +
            Mathf.RoundToInt(averageFoldRate * 100f) + "%" +
            ", 상대공격=" +
            Mathf.RoundToInt(averageAggression * 100f) + "%" +
            ", 상대블러프=" +
            Mathf.RoundToInt(averageBluff * 100f) + "%" +
            ", 교환텔보정=" +
            Mathf.RoundToInt(rangePenalty * 100f) + "%" +
            ", 선택=" + selected.action +
            ", 추가금=" +
            PlayerControl.FormatKoreanMoney(
                selected.additionalAmount
            );
    }

    private static string CreateExchangeReason(
        PokerHandValue currentHand,
        ExchangeCandidate selected,
        IList<int> cards)
    {
        string cardNames = string.Empty;

        for (int i = 0; i < selected.indexes.Count; i++)
        {
            int handIndex = selected.indexes[i];

            if (handIndex < 0 || handIndex >= cards.Count)
            {
                continue;
            }

            if (cardNames.Length > 0)
            {
                cardNames += ", ";
            }

            cardNames += CardUtility.GetCardName(
                cards[handIndex]
            );
        }

        if (selected.indexes.Count == 0)
        {
            cardNames = "없음";
        }

        return
            "현재패=" +
            PokerHandEvaluator.GetCategoryName(
                currentHand.Category
            ) +
            ", 교환=" + cardNames +
            ", 예상점수=" +
            selected.averageScore.ToString("0.000") +
            ", 개선확률=" +
            Mathf.RoundToInt(selected.improvementRate * 100f) + "%" +
            ", 스트레이트이상=" +
            Mathf.RoundToInt(selected.premiumFinishRate * 100f) + "%";
    }

    private static float NextFloat(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static float NextSignedFloat(
        System.Random random)
    {
        return NextFloat(random) * 2f - 1f;
    }
}