using System;
using System.Collections.Generic;

public enum PokerHandCategory
{
    HighCard = 0,
    OnePair = 1,
    TwoPair = 2,
    ThreeOfAKind = 3,
    Straight = 4,
    Flush = 5,
    FullHouse = 6,
    FourOfAKind = 7,
    StraightFlush = 8
}

public class PokerHandValue : IComparable<PokerHandValue>
{
    public PokerHandCategory Category { get; private set; }

    public List<int> TieBreakers { get; private set; }

    public PokerHandValue(
        PokerHandCategory category,
        List<int> tieBreakers)
    {
        Category = category;
        TieBreakers = tieBreakers;
    }

    public int CompareTo(PokerHandValue other)
    {
        if (other == null)
        {
            return 1;
        }

        int categoryCompare =
            Category.CompareTo(other.Category);

        if (categoryCompare != 0)
        {
            return categoryCompare;
        }

        int compareCount = Math.Min(
            TieBreakers.Count,
            other.TieBreakers.Count
        );

        for (int i = 0; i < compareCount; i++)
        {
            int compare =
                TieBreakers[i].CompareTo(
                    other.TieBreakers[i]
                );

            if (compare != 0)
            {
                return compare;
            }
        }

        return TieBreakers.Count.CompareTo(
            other.TieBreakers.Count
        );
    }
}

public static class PokerHandEvaluator
{
    private class RankGroup
    {
        public int rank;
        public int count;

        public RankGroup(int rank, int count)
        {
            this.rank = rank;
            this.count = count;
        }
    }

    public static PokerHandValue Evaluate(
        IList<int> cardNumbers)
    {
        if (cardNumbers == null ||
            cardNumbers.Count != 5)
        {
            throw new ArgumentException(
                "포커 족보 판정에는 정확히 카드 5장이 필요합니다."
            );
        }

        List<int> ranks = new List<int>(5);
        List<int> suits = new List<int>(5);

        Dictionary<int, int> rankCounts =
            new Dictionary<int, int>();

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            int cardNumber = cardNumbers[i];

            if (!CardUtility.IsValidCardNumber(cardNumber))
            {
                throw new ArgumentException(
                    "잘못된 카드 번호입니다: " +
                    cardNumber
                );
            }

            int rank =
                (int)CardUtility.GetRank(cardNumber) + 2;

            int suit =
                (int)CardUtility.GetSuit(cardNumber);

            ranks.Add(rank);
            suits.Add(suit);

            if (!rankCounts.ContainsKey(rank))
            {
                rankCounts.Add(rank, 0);
            }

            rankCounts[rank]++;
        }

        ranks.Sort();
        ranks.Reverse();

        bool isFlush = true;

        for (int i = 1; i < suits.Count; i++)
        {
            if (suits[i] != suits[0])
            {
                isFlush = false;
                break;
            }
        }

        int straightHighRank;
        bool isStraight = TryGetStraightHighRank(
            rankCounts,
            out straightHighRank
        );

        List<RankGroup> groups =
            new List<RankGroup>();

        foreach (
            KeyValuePair<int, int> pair
            in rankCounts)
        {
            groups.Add(
                new RankGroup(
                    pair.Key,
                    pair.Value
                )
            );
        }

        groups.Sort(
            delegate (RankGroup a, RankGroup b)
            {
                int countCompare =
                    b.count.CompareTo(a.count);

                if (countCompare != 0)
                {
                    return countCompare;
                }

                return b.rank.CompareTo(a.rank);
            }
        );

        if (isStraight && isFlush)
        {
            return CreateValue(
                PokerHandCategory.StraightFlush,
                straightHighRank
            );
        }

        if (groups[0].count == 4)
        {
            return CreateValue(
                PokerHandCategory.FourOfAKind,
                groups[0].rank,
                groups[1].rank
            );
        }

        if (groups[0].count == 3 &&
            groups.Count > 1 &&
            groups[1].count == 2)
        {
            return CreateValue(
                PokerHandCategory.FullHouse,
                groups[0].rank,
                groups[1].rank
            );
        }

        if (isFlush)
        {
            return new PokerHandValue(
                PokerHandCategory.Flush,
                new List<int>(ranks)
            );
        }

        if (isStraight)
        {
            return CreateValue(
                PokerHandCategory.Straight,
                straightHighRank
            );
        }

        if (groups[0].count == 3)
        {
            List<int> values = new List<int>();

            values.Add(groups[0].rank);

            List<int> kickers =
                GetRanksWithCount(
                    groups,
                    1
                );

            values.AddRange(kickers);

            return new PokerHandValue(
                PokerHandCategory.ThreeOfAKind,
                values
            );
        }

        if (groups[0].count == 2 &&
            groups.Count > 1 &&
            groups[1].count == 2)
        {
            int highPair = Math.Max(
                groups[0].rank,
                groups[1].rank
            );

            int lowPair = Math.Min(
                groups[0].rank,
                groups[1].rank
            );

            int kicker = 0;

            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].count == 1)
                {
                    kicker = groups[i].rank;
                    break;
                }
            }

            return CreateValue(
                PokerHandCategory.TwoPair,
                highPair,
                lowPair,
                kicker
            );
        }

        if (groups[0].count == 2)
        {
            List<int> values = new List<int>();

            values.Add(groups[0].rank);

            List<int> kickers =
                GetRanksWithCount(
                    groups,
                    1
                );

            values.AddRange(kickers);

            return new PokerHandValue(
                PokerHandCategory.OnePair,
                values
            );
        }

        return new PokerHandValue(
            PokerHandCategory.HighCard,
            new List<int>(ranks)
        );
    }

    private static bool TryGetStraightHighRank(
        Dictionary<int, int> rankCounts,
        out int highRank)
    {
        highRank = 0;

        if (rankCounts.Count != 5)
        {
            return false;
        }

        List<int> uniqueRanks =
            new List<int>(rankCounts.Keys);

        uniqueRanks.Sort();

        // A, 2, 3, 4, 5 스트레이트
        if (uniqueRanks[0] == 2 &&
            uniqueRanks[1] == 3 &&
            uniqueRanks[2] == 4 &&
            uniqueRanks[3] == 5 &&
            uniqueRanks[4] == 14)
        {
            highRank = 5;
            return true;
        }

        for (int i = 1; i < uniqueRanks.Count; i++)
        {
            if (uniqueRanks[i] !=
                uniqueRanks[i - 1] + 1)
            {
                return false;
            }
        }

        highRank =
            uniqueRanks[uniqueRanks.Count - 1];

        return true;
    }

    private static List<int> GetRanksWithCount(
        List<RankGroup> groups,
        int targetCount)
    {
        List<int> result = new List<int>();

        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].count == targetCount)
            {
                result.Add(groups[i].rank);
            }
        }

        result.Sort();
        result.Reverse();

        return result;
    }

    private static PokerHandValue CreateValue(
        PokerHandCategory category,
        params int[] values)
    {
        return new PokerHandValue(
            category,
            new List<int>(values)
        );
    }

    public static string GetCategoryName(
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
                return category.ToString();
        }
    }
}