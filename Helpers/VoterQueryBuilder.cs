namespace VoterVerification.Helpers;

public static class VoterQueryBuilder
{
    public static string Partition(string wardId) =>
        $"PartitionKey eq '{ODataLiteral.Escape(wardId)}'";

    public static string Epic(string wardId, string epicNumber) =>
        $"{Partition(wardId)} and EpicNumber eq '{ODataLiteral.Escape(epicNumber)}'";

    public static string House(string wardId, string houseNumber, string normalizedHouseNumber) =>
        $"{Partition(wardId)} and (NormalizedHouseNumber eq '{ODataLiteral.Escape(normalizedHouseNumber)}' or HouseNumber eq '{ODataLiteral.Escape(houseNumber)}')";

    public static string VerifiedInWard(string wardId) =>
        $"{Partition(wardId)} and IsVerified eq true";
}
