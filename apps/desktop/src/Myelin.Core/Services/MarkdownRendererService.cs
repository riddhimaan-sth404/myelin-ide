using System;
using System.IO;
using System.Text;
using Markdig;

namespace Myelin.Core.Services
{
    public class MarkdownRendererService
    {
        public static readonly MarkdownRendererService Instance = new();

        private readonly MarkdownPipeline _pipeline;

        public MarkdownRendererService()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseAutoLinks()
                .UseTaskLists()
                .UseEmojiAndSmiley()
                .UseMathematics()
                .Build();
        }

        public string RenderToHtml(string markdownText, string title = "Markdown Preview", bool isDarkTheme = true)
        {
            string bodyHtml = Markdown.ToHtml(markdownText ?? "", _pipeline);

            string themeBg = isDarkTheme ? "#1E1E1E" : "#FFFFFF";
            string themeFg = isDarkTheme ? "#CCCCCC" : "#24292F";
            string codeBg = isDarkTheme ? "#2D2D2D" : "#F6F8FA";
            string borderColor = isDarkTheme ? "#3C3C3C" : "#D0D7DE";
            string linkColor = isDarkTheme ? "#58A6FF" : "#0969DA";
            string tableHeaderBg = isDarkTheme ? "#252525" : "#F6F8FA";

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(title)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine($@"
                body {{
                    background-color: {themeBg};
                    color: {themeFg};
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                    font-size: 14px;
                    line-height: 1.6;
                    padding: 24px 32px;
                    max-width: 900px;
                    margin: 0 auto;
                    word-wrap: break-word;
                }}
                h1, h2, h3, h4, h5, h6 {{
                    color: {themeFg};
                    font-weight: 600;
                    margin-top: 24px;
                    margin-bottom: 16px;
                    line-height: 1.25;
                }}
                h1 {{ font-size: 2em; border-bottom: 1px solid {borderColor}; padding-bottom: 0.3em; }}
                h2 {{ font-size: 1.5em; border-bottom: 1px solid {borderColor}; padding-bottom: 0.3em; }}
                h3 {{ font-size: 1.25em; }}
                p, ul, ol, dl, table, pre, blockquote {{ margin-top: 0; margin-bottom: 16px; }}
                a {{ color: {linkColor}; text-decoration: none; }}
                a:hover {{ text-decoration: underline; }}
                code {{
                    font-family: 'Cascadia Code', 'Fira Code', Consolas, Monaco, monospace;
                    font-size: 85%;
                    background-color: {codeBg};
                    padding: 0.2em 0.4em;
                    border-radius: 4px;
                    border: 1px solid {borderColor};
                }}
                pre {{
                    background-color: {codeBg};
                    border: 1px solid {borderColor};
                    border-radius: 6px;
                    padding: 16px;
                    overflow: auto;
                    font-size: 85%;
                    line-height: 1.45;
                }}
                pre code {{
                    background-color: transparent;
                    border: 0;
                    padding: 0;
                    font-size: 100%;
                }}
                blockquote {{
                    margin: 0 0 16px 0;
                    padding: 0 1em;
                    color: #858585;
                    border-left: 0.25em solid {linkColor};
                }}
                table {{
                    border-collapse: collapse;
                    width: 100%;
                    margin-bottom: 16px;
                }}
                table th, table td {{
                    padding: 6px 13px;
                    border: 1px solid {borderColor};
                }}
                table th {{
                    background-color: {tableHeaderBg};
                    font-weight: 600;
                }}
                table tr:nth-child(2n) {{
                    background-color: {codeBg};
                }}
                img {{
                    max-width: 100%;
                    box-sizing: content-box;
                    border-radius: 4px;
                }}
                hr {{
                    height: 1px;
                    background-color: {borderColor};
                    border: none;
                    margin: 24px 0;
                }}
                ul.task-list {{
                    list-style-type: none;
                    padding-left: 0;
                }}
                ul.task-list li {{
                    display: flex;
                    align-items: center;
                    margin-bottom: 4px;
                }}
                ul.task-list input[type='checkbox'] {{
                    margin-right: 8px;
                }}
            ");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine(bodyHtml);
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }
    }
}
