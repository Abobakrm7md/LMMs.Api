import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent {
  prompt: string = '';
  chatHistory: { role: 'user' | 'assistant'; text: string }[] = [];
  isStreaming = false;
  abortController: AbortController | null = null;
  test:string[]=[];
  async sendPrompt() {
    debugger;
    if (!this.prompt.trim()) return;

    this.chatHistory.push({ role: 'user', text: this.prompt });

    let currentResponse = { role: 'assistant' as const, text: '' };
    this.chatHistory.push(currentResponse);

    this.isStreaming = true;
    this.abortController = new AbortController();

    const response = await fetch('https://localhost:7098/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: this.prompt }),
      signal: this.abortController.signal
    });

    const reader = response.body!.getReader();
    const decoder = new TextDecoder();

    while (this.isStreaming) {
      const { done, value } = await reader.read();
      if (done) break;

      const chunk = decoder.decode(value, { stream: true });
      for (const line of chunk.split("\n")) {
        if (!line.trim()) continue;

        try {
          //const obj = JSON.parse(line);

          // Reassign object to trigger Angular update
          currentResponse = {
            ...currentResponse,
            text: currentResponse.text + line
          };
          console.log(currentResponse.text+line);
          this.chatHistory[this.chatHistory.length - 1] = currentResponse;

        } catch {
          console.error("Invalid JSON chunk", line);
        }
      }
    }

    this.isStreaming = false;
    this.prompt = '';
  }

  stopResponse() {
    this.isStreaming = false;
    this.abortController?.abort();
  }
//   formatMessage(text: string): string {
//   // Escape HTML
//   let formatted = text
//     .replace(/&/g, "&amp;")
//     .replace(/</g, "&lt;")
//     .replace(/>/g, "&gt;");

//   // Handle code blocks ```...```
//   formatted = formatted.replace(/```([^`]+)```/g, "<pre><code>$1</code></pre>");

//   // Handle bold **text**
//   formatted = formatted.replace(/\*\*(.*?)\*\*/g, "<strong>$1</strong>");

//   // Handle line breaks
//   formatted = formatted.replace(/\n/g, "<br>");

//   // Handle bullet points
//   formatted = formatted.replace(/^\s*[-*]\s+(.*)$/gm, "• $1");

//   return formatted;
// }

formatMessage(text: string): string {
  if (!text) return "";

  
  // 1. Escape HTML to prevent injection
  let formatted = text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");

  // 2. Handle multi-line code blocks: ```lang\ncode\n```
  formatted = formatted.replace(/```([\s\S]*?)```/g, (match, code) => {
    return `<pre><code>${code.trim()}</code></pre>`;
  });

  // 3. Handle inline code: `code`
  formatted = formatted.replace(/`([^`]+)`/g, "<code>$1</code>");

  // 4. Bold and italic
  formatted = formatted.replace(/\*\*(.*?)\*\*/g, "<strong>$1</strong>");
  formatted = formatted.replace(/\*(.*?)\*/g, "<em>$1</em>");

  // 5. Bulleted lists (- or *) and numbered lists (1.)
  formatted = formatted.replace(
    /(^|\n)(\s*[-*]\s+.+(\n\s*[-*]\s+.+)*)/g,
    (match) => {
      const items = match
        .trim()
        .split(/\n/)
        .map(line => line.replace(/^\s*[-*]\s+/, "").trim())
        .map(item => `<li>${item}</li>`)
        .join("");
      return `<ul>${items}</ul>`;
    }
  );

  formatted = formatted.replace(
    /(^|\n)(\s*\d+\.\s+.+(\n\s*\d+\.\s+.+)*)/g,
    (match) => {
      const items = match
        .trim()
        .split(/\n/)
        .map(line => line.replace(/^\s*\d+\.\s+/, "").trim())
        .map(item => `<li>${item}</li>`)
        .join("");
      return `<ol>${items}</ol>`;
    }
  );

  // 6. Line breaks -> <br>, but not inside <pre> or <ul>/<ol>
  formatted = formatted.replace(/(?<!<\/(pre|ul|ol|li)>)\n/g, "<br>");

  // 7. Convert multiple consecutive newlines into paragraphs
  formatted = formatted.replace(/(<br>\s*){2,}/g, "</p><p>");
  formatted = `<p>${formatted}</p>`;

  return formatted;
}


async formatMessage1(text: string): Promise<string> {
  if (!text) return '';
  return await marked.parse(text);
}

}
