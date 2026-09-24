// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace NetOcc.Generator;

/// <summary>
/// A command's arguments: options with a value, flags, and positional names. Strict: an option the
/// command doesn't take is an error, so a typo can't fall back to a default unnoticed.
/// </summary>
internal sealed class CommandLine
{
  private readonly Dictionary<string, string> _options;
  private readonly HashSet<string> _flags;

  private CommandLine(string command, Dictionary<string, string> options, HashSet<string> flags,
                      List<string> names)
  {
    Command = command;
    _options = options;
    _flags = flags;
    Names = names;
  }

  public string Command { get; }

  /// <summary>The arguments that are neither options nor their values, in order.</summary>
  public IReadOnlyList<string> Names { get; }

  /// <param name="arguments">What follows the command.</param>
  /// <param name="options">The options the command takes, each followed by its value.</param>
  /// <param name="flags">The options without a value.</param>
  public static CommandLine Parse(string command, IReadOnlyList<string> arguments,
                                  IReadOnlyCollection<string> options,
                                  IReadOnlyCollection<string>? flags = null)
  {
    Dictionary<string, string> values = new(StringComparer.Ordinal);
    HashSet<string> set = new(StringComparer.Ordinal);
    List<string> names = [];
    for (var i = 0; i < arguments.Count; i++)
    {
      var argument = arguments[i];
      if (!argument.StartsWith("--", StringComparison.Ordinal))
      {
        names.Add(argument);
      }
      else if (flags?.Contains(argument) == true)
      {
        set.Add(argument);
      }
      else if (!options.Contains(argument))
      {
        throw new ArgumentException($"netocc-gen {command}: unknown option {argument}");
      }
      else if (i + 1 == arguments.Count
               || arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
      {
        throw new ArgumentException($"netocc-gen {command}: {argument} needs a value");
      }
      else
      {
        values[argument] = arguments[++i];
      }
    }

    return new CommandLine(command, values, set, names);
  }

  public string? Option(string name) => _options.GetValueOrDefault(name);

  public string Required(string name) => Option(name)
                                         ?? throw new ArgumentException(
                                           $"netocc-gen {Command}: {name} is required");

  public bool Flag(string name) => _flags.Contains(name);
}
