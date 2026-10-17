using System.Net;
using Avalonia.Media;
using Markdownia.Rendering;
using Markdownia.Theming;

namespace Markdownia.Services;

/// <summary>Exports the document as a standalone HTML page styled to match the current theme and typography.</summary>
public static class HtmlExporter
{
    public static string Export(string markdown, string title, MarkdownTheme theme, AppSettings settings)
    {
        var body = MarkdownService.ToHtml(markdown, settings.SmartTypography);
        static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var width = settings.ContentWidth switch { "Narrow" => "30em", "Wide" => "46em", "Full" => "none", _ => "36em" };
        var serif = settings.PreviewFont is "Literata" or "Source Serif 4" ? "Georgia, serif" : "system-ui, sans-serif";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              :root { color-scheme: {{(theme.IsDark ? "dark" : "light")}}; }
              html { background: {{Hex(theme.Background)}}; }
              body {
                margin: 0 auto; padding: 3rem 1.5rem 5rem; max-width: {{width}};
                font-family: "{{settings.PreviewFont}}", {{serif}};
                font-size: {{settings.PreviewFontSize}}px; line-height: {{settings.LineSpacing}};
                color: {{Hex(theme.Foreground)}};
                text-align: {{(settings.Justify ? "justify" : "left")}};
                text-rendering: optimizeLegibility; font-kerning: normal;
                -webkit-font-smoothing: antialiased;
              }
              h1, h2, h3, h4, h5, h6 { color: {{Hex(theme.Heading)}}; font-weight: 600; line-height: 1.25; margin: 1.6em 0 0.5em; }
              h1 { font-size: 2em; letter-spacing: -0.01em; } h2 { font-size: 1.55em; } h3 { font-size: 1.28em; } h4 { font-size: 1.1em; }
              p, ul, ol, dl, table, pre, blockquote { margin: 0 0 1em; }
              a { color: {{Hex(theme.Accent)}}; text-decoration-thickness: 1px; text-underline-offset: 0.15em; }
              code, pre { font-family: "{{settings.CodeFont}}", ui-monospace, monospace; font-size: 0.86em; }
              code { background: {{Hex(theme.CodeBackground)}}; padding: 0.1em 0.3em; border-radius: 4px; }
              pre { background: {{Hex(theme.CodeBackground)}}; border: 1px solid {{Hex(theme.CodeBorder)}}; border-radius: 6px; padding: 0.9em 1em; overflow-x: auto; line-height: 1.55; }
              pre code { background: none; padding: 0; font-size: 1em; }
              blockquote { border-left: 3px solid {{Hex(theme.QuoteBar)}}; padding-left: 1em; color: {{Hex(theme.Muted)}}; }
              hr { border: 0; border-top: 1px solid {{Hex(theme.Rule)}}; margin: 2em 0; }
              table { border-collapse: collapse; font-size: 0.92em; }
              th, td { border-bottom: 1px solid {{Hex(theme.Rule)}}; padding: 0.45em 0.8em; }
              th { background: {{Hex(theme.TableHeader)}}; font-weight: 600; }
              tr:nth-child(even) td { background: {{Hex(theme.TableStripe)}}; }
              mark { background: {{Hex(theme.Highlight)}}; color: inherit; }
              abbr { text-decoration: underline dotted; }
              img { max-width: 100%; }
              figure { margin: 1.5em 0; text-align: center; } figcaption { color: {{Hex(theme.Muted)}}; font-style: italic; font-size: 0.85em; }
              .footnotes { font-size: 0.86em; border-top: 1px solid {{Hex(theme.Rule)}}; margin-top: 3em; padding-top: 1em; }
              .markdown-alert { border-left: 3px solid {{Hex(theme.Accent)}}; padding: 0.6em 1em; margin-bottom: 1em; border-radius: 0 6px 6px 0; background: {{Hex(theme.CodeBackground)}}; }
              .markdown-alert-title { font-weight: 600; margin-bottom: 0.3em; }
              .task-list-item { list-style: none; } .task-list-item input { margin-right: 0.5em; }
            </style>
            <script>window.MathJax = { tex: { inlineMath: [['\\(', '\\)']], displayMath: [['\\[', '\\]']] } };</script>
            <script async src="https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-chtml.js"></script>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }
}
