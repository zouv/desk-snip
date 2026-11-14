using System.Threading.Tasks;

namespace DeskSnip.App;

internal interface IBackgroundWindow
{
    Task CloseSafelyAsync();
}
