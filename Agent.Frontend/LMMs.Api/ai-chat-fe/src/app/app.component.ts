import { Component, ElementRef, ViewChild } from '@angular/core';
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
  @ViewChild('chatBox') private chatBox?: ElementRef<HTMLDivElement>;
  prompt: string = '';
  chatHistory: { role: 'user' | 'assistant'; text: string }[] = [];
  isStreaming = false;
  abortController: AbortController | null = null;

  // ✅ الجديد
  selectedFile: File | null = null;
  enableTools = false;
  enableWebSearch = false;

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) {
      this.selectedFile = input.files[0];
    }
  }

  removeFile() {
    this.selectedFile = null;
  }

  async sendPrompt() {
    if (!this.prompt.trim() && !this.selectedFile) return;

    this.chatHistory.push({ role: 'user', text: this.prompt });
    let currentResponse = { role: 'assistant' as const, text: '' };
    this.chatHistory.push(currentResponse);
    this.scrollToBottom();

    this.isStreaming = true;
    this.abortController = new AbortController();

    // ✅ لو في ملف استخدم FormData، لو مفيش استخدم JSON عادي
    let body: FormData | string;
    let headers: Record<string, string> = {};
    
      const formData = new FormData();
      formData.append('prompt', this.prompt);
      formData.append('enableTools', String(this.enableTools));
      formData.append('enableWebSearch', String(this.enableTools && this.enableWebSearch));
    if (this.selectedFile) {

      formData.append('file', this.selectedFile);
      // ❌ متحطش Content-Type مع FormData — المتصفح بيحطه تلقائياً مع الـ boundary
    } 
      body = formData;

    const response = await fetch('https://localhost:7098/api/chat', {
      method: 'POST',
      headers,
      body: formData,
      signal: this.abortController.signal
    });

    if (!response.ok || !response.body) {
      currentResponse = {
        ...currentResponse,
        text: `Request failed: ${response.status} ${response.statusText}`
      };
      this.chatHistory[this.chatHistory.length - 1] = currentResponse;
      this.isStreaming = false;
      return;
    }

    const reader = response.body!.getReader();
    const decoder = new TextDecoder();
    let streamedText = '';

    try {
      while (this.isStreaming) {
        const { done, value } = await reader.read();
        if (done) break;
        if (!value) continue;

        // Preserve all whitespace/newlines so markdown lists and paragraphs stay readable.
        streamedText += decoder.decode(value, { stream: true });
        currentResponse = { ...currentResponse, text: streamedText };
        this.chatHistory[this.chatHistory.length - 1] = currentResponse;
        this.scrollToBottom();
      }
    } finally {
      streamedText += decoder.decode();
      currentResponse = { ...currentResponse, text: streamedText };
      this.chatHistory[this.chatHistory.length - 1] = currentResponse;
      this.scrollToBottom();
    }

    this.isStreaming = false;
    this.prompt = '';
    this.selectedFile = null; // ✅ امسح الملف بعد الإرسال
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
  if (!text) return '';
  return marked.parse(text, { gfm: true, breaks: true }) as string;
}

private scrollToBottom(): void {
  requestAnimationFrame(() => {
    const element = this.chatBox?.nativeElement;
    if (!element) return;
    element.scrollTop = element.scrollHeight;
  });
}


}
