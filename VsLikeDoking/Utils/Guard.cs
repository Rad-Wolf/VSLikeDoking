using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VsLikeDoking.Utils
{
  /// <summary>
  /// 공통 인자 / 상태 검증 유틸리티
  /// </summary>
  public class Guard
  {
    // Core =====================================================================

    /// <summary>
    /// 값이 Null이 아니어야 합니다.
    /// </summary>
    public static T NotNull<T>(T? value, string? message = null, [CallerArgumentExpression("value")] string? paramName = null) where T : class
    {
      if (value is null) throw new ArgumentNullException(paramName, message);
      return value;
    }

    /// <summary>
    /// 값이 Null이 아니어야 합니다.
    /// </summary>
    public static T NotNull<T>(T? value, string? message = null, [CallerArgumentExpression("value")] string? paramName = null) where T : struct
    {
      if (!value.HasValue) throw new ArgumentNullException(paramName, message);
      return value.Value;
    }

    /// <summary>
    /// 문자열이 null/빈값/공백이 아니어야 합니다.
    /// </summary>
    public static string NotNullOrWhiteSpace(string? value, string? message = null, [CallerArgumentExpression("value")] string? paramName = null)
    {
      if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message ?? "문자열이 null/빈값/공백이 아니어야 한다.", paramName);
      return value;
    }

    /// <summary>
    /// 컬랙션이 null이 아니어야 합니다.
    /// </summary>
    public static TCollection NotNull<TCollection, TItem>(TCollection? value, string? message = null, [CallerArgumentExpression("value")] string? paramName = null) where TCollection : class, IReadOnlyCollection<TItem>
    {
      if (value is null) throw new ArgumentNullException(paramName, message);
      return value;
    }

    /// <summary>
    /// 컬랙션이 null이 아니고 비어있지 않아야 합니다.
    /// </summary>
    public static TCollection NotNullOrEmty<TCollection, TItem>(TCollection? value, string? message = null, [CallerArgumentExpression("value")] string? paramName = null) where TCollection : class, IReadOnlyCollection<TItem>
    {
      if (value is null) throw new ArgumentNullException(paramName, message);
      if (value.Count == 0) throw new ArgumentException(message ?? "컬랙션이 비어있습니다.", paramName);
      return value;
    }

    /// <summary>
    /// 값이 지정 범위 안에 있어야 합니다. [초과,미만]
    /// </summary>
    public static int InRange(int value, int minInclusive, int maxInclusive, string? message = null, [CallerArgumentExpression("value")] string? paramName = null)
    {
      if (value < minInclusive || value > maxInclusive) throw new ArgumentOutOfRangeException(paramName, value, message ?? $"값이 [{minInclusive},{maxInclusive}] 범위에 있어야 합니다.");
      return value;
    }

    /// <summary>
    /// 값이 지정 범위 안에 있어야 합니다. [초과,미만]
    /// </summary>
    public static double InRange(double value, double minInclusive, double maxInclusive, string? message = null, [CallerArgumentExpression("value")] string? paramName = null)
    {
      if (double.IsNaN(value) || value < minInclusive || value > maxInclusive) throw new ArgumentOutOfRangeException(paramName, value, message ?? $"값이 [{minInclusive},{maxInclusive}] 안에 있어야 합니다.");
      return value;
    }

    /// <summary>
    /// 값이 0 이상이어야 합니다.
    /// </summary>
    public static int NonNegative(int value, string? message = null, [CallerArgumentExpression("value")] string? paramName = null)
    {
      if (value < 0) throw new ArgumentOutOfRangeException(paramName, value, message ?? "값이 0 이상이어야 합니다.");
      return value;
    }

    /// <summary>
    /// 값이 0을 초과해야 합니다.
    /// </summary>
    public static int Positive(int value, string? message = null, [CallerArgumentExpression("value")] string? paramName = null)
    {
      if (value <= 0) throw new ArgumentOutOfRangeException(paramName, value, message ?? "값이 0을 초과해야합니다.");
      return value;
    }

    /// <summary>
    /// 조건이 참이어야 합니다. (인자 검증용)
    /// </summary>
    public static void Requires(bool condition, string message, [CallerArgumentExpression("condition")] string? paramName = null)
    {
      if (condition) return;
      throw new ArgumentException(message, paramName);
    }

    /// <summary>
    /// 조건이 참이어야 합니다.(상태검증용)
    /// </summary>
    public static void RequiresState(bool condition, string message)
    {
      if (condition) return;
      throw new InvalidOperationException(message);
    }
  }
}