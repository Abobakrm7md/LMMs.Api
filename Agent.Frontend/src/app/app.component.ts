import { Component, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';

interface SpeechRecognitionEventLike extends Event {
  resultIndex: number;
  results: SpeechRecognitionResultList;
}

interface SpeechRecognitionLike extends EventTarget {
  lang: string;
  interimResults: boolean;
  continuous: boolean;
  onresult: ((event: SpeechRecognitionEventLike) => void) | null;
  onerror: ((event: Event) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
}

interface BrowserWindowWithSpeech extends Window {
  webkitSpeechRecognition?: new () => SpeechRecognitionLike;
  SpeechRecognition?: new () => SpeechRecognitionLike;
}

interface ChatMessage {
  role: 'user' | 'assistant';
  text: string;
  createdAt: Date;
}

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
  chatHistory: ChatMessage[] = [];
  isStreaming = false;
  abortController: AbortController | null = null;
  isListening = false;
  voiceError = '';
  copiedMessageIndex: number | null = null;
  speakingMessageIndex: number | null = null;
  private speechRecognition: SpeechRecognitionLike | null = null;
  private dictationSeedPrompt = '';

  // ✅ الجديد
  selectedFile: File | null = null;
  /** When true, API registers tools (calculator, time, …). Off by default so the model answers in plain text. */
  enableTools = false;
  /** When enableTools is true, also expose web search. */
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

  toggleVoiceInput() {
    if (this.isListening) {
      this.stopVoiceInput();
      return;
    }
    this.startVoiceInput();
  }

  private startVoiceInput() {
    this.voiceError = '';
    const browserWindow = window as BrowserWindowWithSpeech;
    const SpeechRecognitionCtor = browserWindow.SpeechRecognition || browserWindow.webkitSpeechRecognition;

    if (!SpeechRecognitionCtor) {
      this.voiceError = 'Voice input is not supported in this browser.';
      return;
    }

    this.speechRecognition = new SpeechRecognitionCtor();
    this.dictationSeedPrompt = this.prompt.trim();
    this.speechRecognition.lang = 'en-US';
    this.speechRecognition.interimResults = true;
    this.speechRecognition.continuous = true;

    this.speechRecognition.onresult = (event: SpeechRecognitionEventLike) => {
      let completeTranscript = '';

      for (let i = 0; i < event.results.length; i++) {
        const result = event.results[i];
        const text = result[0]?.transcript ?? '';
        completeTranscript += `${text} `;
      }

      // Rebuild dictated text from recognition snapshot to avoid duplicated phrases.
      this.prompt = this.joinPrompts(
        this.dictationSeedPrompt,
        completeTranscript.trim()
      );
    };

    this.speechRecognition.onerror = () => {
      this.voiceError = 'Microphone permission denied or speech recognition failed.';
      this.isListening = false;
    };

    this.speechRecognition.onend = () => {
      this.isListening = false;
    };

    this.speechRecognition.start();
    this.isListening = true;
  }

  private stopVoiceInput() {
    this.speechRecognition?.stop();
    this.isListening = false;
    this.dictationSeedPrompt = this.prompt.trim();
  }

  private joinPrompts(base: string, incoming: string): string {
    const left = base.trim();
    const right = incoming.trim();
    if (!left) return right;
    if (!right) return left;
    return `${left} ${right}`;
  }

  async copyResponse(text: string, index: number) {
    if (!text.trim()) return;
    try {
      await navigator.clipboard.writeText(text);
      this.copiedMessageIndex = index;
      setTimeout(() => {
        if (this.copiedMessageIndex === index) this.copiedMessageIndex = null;
      }, 1500);
    } catch {
      this.voiceError = 'Failed to copy message.';
    }
  }

  toggleReadAloud(text: string, index: number) {
    if (!text.trim() || !('speechSynthesis' in window)) {
      this.voiceError = 'Text-to-speech is not supported in this browser.';
      return;
    }

    if (this.speakingMessageIndex === index) {
      window.speechSynthesis.cancel();
      this.speakingMessageIndex = null;
      return;
    }

    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = 'en-US';
    utterance.onend = () => {
      this.speakingMessageIndex = null;
    };
    utterance.onerror = () => {
      this.voiceError = 'Unable to read this response aloud.';
      this.speakingMessageIndex = null;
    };
    this.speakingMessageIndex = index;
    window.speechSynthesis.speak(utterance);
  }

  async sendPrompt() {
    if (!this.prompt.trim() && !this.selectedFile) return;
    if (this.isListening) this.stopVoiceInput();
    const userPrompt = this.prompt;

    this.chatHistory.push({ role: 'user', text: userPrompt, createdAt: new Date() });
    let currentResponse: ChatMessage = { role: 'assistant', text: '', createdAt: new Date() };
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
      currentResponse = {
        ...currentResponse,
        text: streamedText
      };
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

formatMessageTime(date: Date): string {
  return new Intl.DateTimeFormat([], {
    hour: '2-digit',
    minute: '2-digit'
  }).format(date);
}

private scrollToBottom(): void {
  requestAnimationFrame(() => {
    const element = this.chatBox?.nativeElement;
    if (!element) return;
    element.scrollTop = element.scrollHeight;
  });
}


}
