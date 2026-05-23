using MegaCrit.Sts2.Core.Models;

namespace ModHotReload.Runtime;

/// <summary>过滤编译器生成类型，只处理 AbstractModel 子类。</summary>
internal static class ModelDbTypeFilter
{
    internal static bool IsLikelyModelType(Type? type)
    {
        if (type == null || type.IsAbstract || type.IsInterface)
            return false;

        if (type.Name.Contains('<', StringComparison.Ordinal))
            return false;

        if (type.Name.StartsWith("__", StringComparison.Ordinal))
            return false;

        return typeof(AbstractModel).IsAssignableFrom(type);
    }
}
