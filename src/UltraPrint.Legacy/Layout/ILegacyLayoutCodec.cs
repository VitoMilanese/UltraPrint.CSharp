using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Layout;

public interface ILegacyLayoutCodec
{
    CardLayout Load(string path);
    void Save(CardLayout layout, string path);
}
