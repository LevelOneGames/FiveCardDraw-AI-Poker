using System;

public enum CardSuit
{
    Club = 0,
    Diamond = 1,
    Heart = 2,
    Spade = 3
}

public enum CardRank
{
    Two = 0,
    Three = 1,
    Four = 2,
    Five = 3,
    Six = 4,
    Seven = 5,
    Eight = 6,
    Nine = 7,
    Ten = 8,
    Jack = 9,
    Queen = 10,
    King = 11,
    Ace = 12
}

public static class CardUtility
{
    public const int TotalCardCount = 52;
    public const int CardsPerSuit = 13;

    private static readonly string[] RankNames =
    {
        "2", "3", "4", "5", "6", "7", "8",
        "9", "10", "J", "Q", "K", "A"
    };

    private static readonly string[] SuitNames =
    {
        "♣", "♦", "♥", "♠"
    };

    /*
     * 카드 번호 규칙
     *
     * 0~12  : 클로버 2~A
     * 13~25 : 다이아몬드 2~A
     * 26~38 : 하트 2~A
     * 39~51 : 스페이드 2~A
     *
     * 예:
     * 0  = 2♣
     * 12 = A♣
     * 13 = 2♦
     * 25 = A♦
     * 26 = 2♥
     * 38 = A♥
     * 39 = 2♠
     * 51 = A♠
     */

    public static CardSuit GetSuit(int cardNumber)
    {
        ValidateCardNumber(cardNumber);
        return (CardSuit)(cardNumber / CardsPerSuit);
    }

    public static CardRank GetRank(int cardNumber)
    {
        ValidateCardNumber(cardNumber);
        return (CardRank)(cardNumber % CardsPerSuit);
    }

    public static string GetCardName(int cardNumber)
    {
        ValidateCardNumber(cardNumber);

        int suitIndex = cardNumber / CardsPerSuit;
        int rankIndex = cardNumber % CardsPerSuit;

        return RankNames[rankIndex] + SuitNames[suitIndex];
    }

    public static int GetCardNumber(CardSuit suit, CardRank rank)
    {
        return ((int)suit * CardsPerSuit) + (int)rank;
    }

    public static bool IsValidCardNumber(int cardNumber)
    {
        return cardNumber >= 0 && cardNumber < TotalCardCount;
    }

    private static void ValidateCardNumber(int cardNumber)
    {
        if (!IsValidCardNumber(cardNumber))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cardNumber),
                cardNumber,
                "카드 번호는 0~51 사이여야 합니다."
            );
        }
    }
}