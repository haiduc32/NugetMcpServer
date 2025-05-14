using System.Linq;

namespace NuGetMcpServer.Services;

/// <summary>
/// Model for interface information
/// </summary>
public class InterfaceInfo
{
    /// <summary>
    /// Interface name (without namespace)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full interface name with namespace
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Assembly name where interface is defined
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;
    
    /// <summary>
    /// Returns a formatted name for display, converting generic notation from `N to <T>
    /// </summary>
    /// <returns>Formatted interface name</returns>
    public string GetFormattedName()
    {
        // Проверка является ли интерфейс дженерик типом
        var tickIndex = Name.IndexOf('`');
        if (tickIndex <= 0)
            return Name;
            
        // Форматируем имя дженерик типа (например, IMaze`1 -> IMaze<T>)
        var baseName = Name.Substring(0, tickIndex);
        var numGenericArgs = int.Parse(Name.Substring(tickIndex + 1));
        
        // Создаем обобщенные параметры T, U, V, ... в зависимости от количества
        var genericArgs = string.Join(", ", Enumerable.Range(0, numGenericArgs).Select(GetGenericParamName));
        
        return $"{baseName}<{genericArgs}>";
    }
    
    /// <summary>
    /// Returns a formatted full name with namespace, converting generic notation
    /// </summary>
    /// <returns>Formatted full interface name with namespace</returns>
    public string GetFormattedFullName()
    {
        // Работаем с полным именем, которое включает пространство имен
        var lastDot = FullName.LastIndexOf('.');
        if (lastDot <= 0) 
            return GetFormattedName();
            
        var ns = FullName.Substring(0, lastDot + 1); // включаем точку
        var name = FullName.Substring(lastDot + 1);
        
        var tickIndex = name.IndexOf('`');
        if (tickIndex <= 0)
            return FullName;
            
        var baseName = name.Substring(0, tickIndex);
        var numGenericArgs = int.Parse(name.Substring(tickIndex + 1));
        
        var genericArgs = string.Join(", ", Enumerable.Range(0, numGenericArgs).Select(GetGenericParamName));
        
        return $"{ns}{baseName}<{genericArgs}>";
    }
    
    /// <summary>
    /// Helper to get generic parameter names (T, U, V, etc.)
    /// </summary>
    private static string GetGenericParamName(int index)
    {
        // Для первых 26 параметров используем буквы T, U, V, ...
        if (index < 26)
            return ((char)('T' + index)).ToString();
        
        // Для большего числа параметров добавляем индекс
        return $"T{index}";
    }
}
