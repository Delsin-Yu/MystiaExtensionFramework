namespace Mystia;

/// <summary>Guest invitation records, backed by the game's status tracker. Usable outside any scene loop.</summary>
public interface IGuestRecords
{
    void RecordInvited(int guestId);

    bool HasInvited(int guestId);

    bool IsIgnored(int guestId);

    void Reset();
}
