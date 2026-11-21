using VoterVerification.Models;

namespace VoterVerification.Services;

public sealed class ActiveWardContext
{
    public WardOption? Current { get; set; }
}
