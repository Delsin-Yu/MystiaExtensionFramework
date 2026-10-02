using Mystia.Listeners;
using NightScene.GuestManagementUtility;

namespace SampleMod.Skip;

public sealed class SkipNormalSpawn : IGuestDirector
{
    public IGuestDriver? Claim(GuestGroupController group) => new IdleDriver();

    private sealed class IdleDriver : IGuestDriver
    {
        public void Start(IGuestControls controls)
        {
        }
    }
}
