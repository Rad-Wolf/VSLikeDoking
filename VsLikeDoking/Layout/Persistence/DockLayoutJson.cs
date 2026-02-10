using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VsLikeDoking.Layout.Persistence
{
  /// <summary>DTO(DockLayoutDto)를 JSON 문자열/파일로 저장하고, JSon에서 DTO를 읽어오는 I/O 전용</summary>
  /// <remarks>트리↔DTO 변환은 DockLayoutSerializer가 담당한다. </remarks>
  public static class DockLayoutJson
  {
    // Options ==================================================================

    /// <summary>기본 JSON 옵션을 생성한다.</summary>
    /// <remarks>기본 : Write Indented = true, Enum은 숫자로 저장</remarks>
    public static JsonSerializerOptions CreateDefaultOptions(bool writeIndented = true)
    {
      return new JsonSerializerOptions { WriteIndented = writeIndented, PropertyNameCaseInsensitive = true };
    }

    // Save/Load (File) ==========================================================

    /// <summary>DTO를 JSON파일로 저장한다. 디렉터리가 없으면 생성한다.</summary>
    public static void SaveToFile(string path, DockLayoutDto dto, JsonSerializerOptions? options = null)
    {
      if (dto is null) throw new ArgumentNullException(nameof(dto));
      if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path가 비어있습니다.", nameof(path));

      options ??= CreateDefaultOptions(true);

      var dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

      var json = JsonSerializer.Serialize(dto, options);
      File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>JSON 파일에서 DTO를 읽어온다. 실패 시 예외를 던진다.</summary>
    public static DockLayoutDto LoadFromFile(string path, JsonSerializerOptions? options = null)
    {
      if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path가 비어있습니다.", nameof(path));

      options ??= CreateDefaultOptions(true);

      var json = File.ReadAllText(path, Encoding.UTF8);
      var dto = JsonSerializer.Deserialize<DockLayoutDto>(json, options);
      if (dto is null) throw new InvalidDataException("DockLayoutDto를 역직렬화하지 못했습니다.");

      return dto;
    }

    /// <summary>JSON 파일에서 DTO 읽기를 시도한다. 실패하면 false</summary>
    /// <returns></returns>
    public static bool TryLoadFromFile(string path, out DockLayoutDto? dto, JsonSerializerOptions? options = null)
    {
      dto = null;

      if (string.IsNullOrWhiteSpace(path)) return false;
      if (!File.Exists(path)) return false;

      try
      {
        dto = LoadFromFile(path, options);
        return true;
      }
      catch
      {
        dto = null;
        return false;
      }
    }

    // Save/Load (String) ========================================================

    /// <summary>DTO를 JSON 문자열로 직렬화한다.</summary>
    public static string SaveToString(DockLayoutDto dto, bool writeIndented = true, JsonSerializerOptions? options = null)
    {
      if (dto is null) throw new ArgumentNullException(nameof(dto));

      options ??= CreateDefaultOptions(writeIndented);
      return JsonSerializer.Serialize(dto, options);
    }

    /// <summary>JSON 문자열에서 DTO를 역직렬화한다. 실패 시 예외를 던진다.</summary>
    public static DockLayoutDto LoadFromString(string json, JsonSerializerOptions? options = null)
    {
      if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Json이 비어있습니다.", nameof(json));
      options ??= CreateDefaultOptions(true);

      var dto = JsonSerializer.Deserialize<DockLayoutDto>(json, options);
      if (dto is null) throw new InvalidDataException("DockLayoutDto를 역직렬화하지 못했습니다.");

      return dto;
    }

    /// <summary>JSON 문자열에서 DTO 역직렬화를 시도한다. 실패하면 false</summary>
    public static bool TryLoadFromString(string? json, out DockLayoutDto? dto, JsonSerializerOptions? options = null)
    {
      dto = null;

      if (string.IsNullOrWhiteSpace(json)) return false;

      try
      {
        dto = LoadFromString(json, options);
        return true;
      }
      catch
      {
        dto = null;
        return false;
      }
    }
  }
}