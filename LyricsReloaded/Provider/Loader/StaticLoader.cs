/*
    Copyright 2013 Phillip Schichtel

    This file is part of LyricsReloaded.

    LyricsReloaded is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    LyricsReloaded is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with LyricsReloaded. If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace CubeIsland.LyricsReloaded.Provider.Loader
{
  public class StaticLoader : LyricsLoader
  {
    private readonly LyricsReloaded lyricsReloaded;
    private readonly string urlTemplate;
    private readonly Pattern pattern;
    private readonly WebClient client;

    public StaticLoader(LyricsReloaded lyricsReloaded, WebClient client, string urlTemplate, Pattern pattern)
    {
      this.lyricsReloaded = lyricsReloaded;
      this.urlTemplate = urlTemplate;
      this.pattern = pattern;
      this.client = client;
    }

    private string constructUrl(Dictionary<string, string> variables)
    {
      string url = urlTemplate;

      foreach (KeyValuePair<string, string> entry in variables)
      {
        url = url.Replace("{" + entry.Key + "}", entry.Value);
      }

      return url;
    }

    public Lyrics getLyrics(Provider provider, Dictionary<string, string> variables)
    {
      string url = constructUrl(variables);

      lyricsReloaded.getLogger().debug("The constructed URL: {0}", url);

      string title = "";
      if (!variables.TryGetValue("title", out title))
      {
        throw new Exception("invalid title");
      }
      lyricsReloaded.getLogger().debug("The title: {0}", title);

      try
      {
        WebResponse response = client.get(url, provider.getHeaders());
        String lyrics = pattern.apply(response.getContent(), title);
        if (lyrics == null)
        {
          lyricsReloaded.getLogger().warn("The pattern {0} didn't match!", pattern);
          return null;
        }

        return new Lyrics(lyrics, response.getEncoding());
      }
      catch (WebException e)
      {
        if (isStatus(e, HttpStatusCode.NotFound))
        {
          return null;
        }
        throw;
      }
    }

    private static bool isStatus(WebException e, HttpStatusCode code)
    {
      System.Net.WebResponse r = e.Response;
      if (r == null)
      {
        if (e.InnerException != null && e.InnerException is WebException)
        {
          return isStatus(e.InnerException as WebException, code);
        }
      }
      else if (r is HttpWebResponse && ((HttpWebResponse)r).StatusCode == code)
      {
        return true;
      }
      return false;
    }
  }

  public class StaticLoaderFactory : LyricsLoaderFactory
  {
    private static class Node
    {
      public static readonly YamlScalarNode URL = new YamlScalarNode("url");
      public static readonly YamlScalarNode PATTERN = new YamlScalarNode("pattern");
    }

    private readonly LyricsReloaded lyricsReloaded;
    private readonly WebClient webClient;

    public StaticLoaderFactory(LyricsReloaded lyricsReloaded)
    {
      this.lyricsReloaded = lyricsReloaded;
      webClient = new WebClient(lyricsReloaded, 5000);
    }

    public string getName()
    {
      return "static";
    }

    public LyricsLoader newLoader(YamlMappingNode configuration)
    {
      YamlNode node;
      IDictionary<YamlNode, YamlNode> configNodes = configuration.Children;

      string url;
      node = (configNodes.ContainsKey(Node.URL) ? configNodes[Node.URL] : null);
      if (node is YamlScalarNode)
      {
        url = ((YamlScalarNode)node).Value;
      }
      else
      {
        throw new InvalidConfigurationException("No URL specified!");
      }

      if (!configNodes.ContainsKey(Node.PATTERN))
      {
        throw new InvalidConfigurationException("No pattern specified!");
      }

      return new StaticLoader(lyricsReloaded, webClient, url, Pattern.fromYamlNode(configNodes[Node.PATTERN]));
    }
  }

  public class Pattern
  {
    private static class Node
    {
      public static readonly YamlScalarNode REGEX = new YamlScalarNode("regex");
      public static readonly YamlScalarNode OPTIONS = new YamlScalarNode("options");
    }

    private const RegexOptions DEFAULT_OPTIONS = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant;

    private readonly string regexString;
    private readonly RegexOptions regexOptions;
    private readonly Regex regex;

    public Pattern(string regex, RegexOptions regexOptions)
    {
      this.regexString = regex;
      this.regexOptions = regexOptions;
      this.regex = new Regex(regex, regexOptions);
    }

    public Pattern(string regex, string options)
        : this(regex, DEFAULT_OPTIONS | regexOptionsFromString(options))
    { }

    public String apply(string content, string title)
    {
      var regexWithTitleReplaced = this.regexString.Replace("{title}", title);
      var regex = regexWithTitleReplaced != this.regexString ? new Regex(regexWithTitleReplaced, this.regexOptions) : this.regex;

      var output = new StringBuilder();
      var matches = regex.Matches(content);
      for (var i = 0; i < matches.Count; i += 1)
      {
        var match = matches[i];
        if (match.Success)
        {
          output.AppendLine(match.Groups["lyrics"].ToString());
        }
      }

      var result = output.ToString();
      if (string.IsNullOrEmpty(result))
      {
        return null;
      }
      else
      {
        return result;
      }
    }

    public static Pattern fromYamlNode(YamlNode node)
    {
      if (node == null)
      {
        return null;
      }
      var result = regexFromYamlNode(node, DEFAULT_OPTIONS);
      return new Pattern(result.Key, result.Value);
    }

    public static KeyValuePair<string, RegexOptions> regexFromYamlNode(YamlNode node, RegexOptions options)
    {
      string regex;
      string regexOptions = "";
      if (node is YamlScalarNode)
      {
        regex = ((YamlScalarNode)node).Value;
      }
      else if (node is YamlSequenceNode)
      {
        IEnumerator<YamlNode> it = ((YamlSequenceNode)node).Children.GetEnumerator();
        if (!it.MoveNext())
        {
          throw new InvalidConfigurationException("The pattern needs at least the regex defined!");
        }
        if (!(it.Current is YamlScalarNode))
        {
          throw new InvalidConfigurationException("The pattern may only contain string value!");
        }
        regex = ((YamlScalarNode)it.Current).Value;

        if (it.MoveNext() && it.Current is YamlScalarNode)
        {
          regexOptions = ((YamlScalarNode)it.Current).Value;
        }
      }
      else if (node is YamlMappingNode)
      {
        IDictionary<YamlNode, YamlNode> patternConfig = ((YamlMappingNode)node).Children;
        node = (patternConfig.ContainsKey(Node.REGEX) ? patternConfig[Node.REGEX] : null);
        if (!(node is YamlScalarNode))
        {
          throw new InvalidConfigurationException("Invalid regex value!");
        }
        regex = ((YamlScalarNode)node).Value;

        node = (patternConfig.ContainsKey(Node.OPTIONS) ? patternConfig[Node.OPTIONS] : null);
        if (node is YamlScalarNode)
        {
          regexOptions = ((YamlScalarNode)node).Value;
        }
      }
      else
      {
        throw new InvalidConfigurationException("No pattern specified!");
      }

      return new KeyValuePair<string, RegexOptions>(regex, options | regexOptionsFromString(regexOptions));
    }

    private static readonly Dictionary<char, RegexOptions> REGEX_OPTION_MAP = new Dictionary<char, RegexOptions> {
            {'i', RegexOptions.IgnoreCase},
            {'s', RegexOptions.Singleline},
            {'m', RegexOptions.Multiline},
            {'c', RegexOptions.Compiled},
            {'x', RegexOptions.IgnorePatternWhitespace},
            {'d', RegexOptions.RightToLeft},
            {'e', RegexOptions.ExplicitCapture},
            {'j', RegexOptions.ECMAScript},
            {'l', RegexOptions.CultureInvariant}
        };

    public static RegexOptions regexOptionsFromString(string optionString)
    {
      RegexOptions options = RegexOptions.None;

      char lc;
      foreach (char c in optionString)
      {
        lc = Char.ToLower(c);
        if (REGEX_OPTION_MAP.ContainsKey(lc))
        {
          RegexOptions option = REGEX_OPTION_MAP[lc];
          if (Char.IsLower(c))
          {
            options |= option;
          }
          else
          {
            options &= ~option;
          }
        }
      }

      return options;
    }

    public override string ToString()
    {
      return regex.ToString();
    }
  }

}
