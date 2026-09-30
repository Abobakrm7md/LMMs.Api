import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';

interface CurrentUser {
  id: string;
  email: string;
  userName: string;
  displayName: string;
}

interface AuthResponse {
  token: { value: string; expiresAt: string };
  user: CurrentUser;
}

interface ConversationSummary {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  lastMessageAt?: string | null;
}

interface ChatMessage {
  id: string;
  sequenceNumber: number;
  role: 'User' | 'Assistant';
  status: 'Streaming' | 'Completed' | 'Cancelled' | 'Failed';
  content: string;
  createdAt: string;
  updatedAt: string;
  modelName?: string | null;
}

interface ConversationDetail extends ConversationSummary {
  messagePage: { messages: ChatMessage[]; nextBeforeSequence?: number | null };
}

interface StreamEvent {
  type: string;
  userMessageId?: string;
  assistantMessageId?: string;
  text?: string;
  status?: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  private readonly apiBase = '/api';
  private readonly tokenStorageKey = 'lmms.access-token';

  authMode: 'login' | 'register' = 'login';
  email = '';
  userName = '';
  displayName = '';
  password = '';
  authError = '';
  isAuthenticating = false;

  token = '';
  currentUser: CurrentUser | null = null;
  conversations: ConversationSummary[] = [];
  selectedConversation: ConversationSummary | null = null;
  messages: ChatMessage[] = [];
  nextBeforeSequence: number | null = null;
  prompt = '';
  isLoadingConversations = false;
  isLoadingMessages = false;
  isStreaming = false;
  error = '';
  private abortController: AbortController | null = null;

  async ngOnInit(): Promise<void> {
    this.token = sessionStorage.getItem(this.tokenStorageKey) ?? '';
    if (this.token) {
      await this.loadConversations();
    }
  }

  get isAuthenticated(): boolean {
    return !!this.token;
  }

  async submitAuthentication(): Promise<void> {
    this.authError = '';
    this.isAuthenticating = true;
    try {
      const endpoint = this.authMode === 'register' ? '/auth/register' : '/auth/login';
      const payload = this.authMode === 'register'
        ? { email: this.email, userName: this.userName, displayName: this.displayName, password: this.password }
        : { emailOrUserName: this.email, password: this.password };
      const response = await this.api<AuthResponse>(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      }, false);
      this.token = response.token.value;
      this.currentUser = response.user;
      sessionStorage.setItem(this.tokenStorageKey, this.token);
      this.password = '';
      await this.loadConversations();
    } catch (error) {
      this.authError = this.errorMessage(error, 'Unable to authenticate.');
    } finally {
      this.isAuthenticating = false;
    }
  }

  switchAuthMode(mode: 'login' | 'register'): void {
    this.authMode = mode;
    this.authError = '';
  }

  logout(): void {
    this.abortController?.abort();
    this.token = '';
    this.currentUser = null;
    this.conversations = [];
    this.selectedConversation = null;
    this.messages = [];
    this.prompt = '';
    sessionStorage.removeItem(this.tokenStorageKey);
  }

  async loadConversations(): Promise<void> {
    if (!this.token) return;
    this.isLoadingConversations = true;
    this.error = '';
    try {
      this.conversations = await this.api<ConversationSummary[]>('/conversations?take=50');
      if (this.conversations.length && !this.selectedConversation) {
        await this.selectConversation(this.conversations[0]);
      }
    } catch (error) {
      this.error = this.errorMessage(error, 'Unable to load conversations.');
    } finally {
      this.isLoadingConversations = false;
    }
  }

  async createConversation(): Promise<void> {
    this.error = '';
    try {
      const conversation = await this.api<ConversationSummary>('/conversations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({})
      });
      this.conversations = [conversation, ...this.conversations];
      this.selectedConversation = conversation;
      this.messages = [];
      this.nextBeforeSequence = null;
    } catch (error) {
      this.error = this.errorMessage(error, 'Unable to create a conversation.');
    }
  }

  async selectConversation(conversation: ConversationSummary): Promise<void> {
    if (this.isStreaming || this.selectedConversation?.id === conversation.id) return;
    this.selectedConversation = conversation;
    this.isLoadingMessages = true;
    this.error = '';
    try {
      const detail = await this.api<ConversationDetail>(`/conversations/${conversation.id}?take=100`);
      this.selectedConversation = this.toSummary(detail);
      this.messages = detail.messagePage.messages;
      this.nextBeforeSequence = detail.messagePage.nextBeforeSequence ?? null;
    } catch (error) {
      this.error = this.errorMessage(error, 'Unable to load this conversation.');
    } finally {
      this.isLoadingMessages = false;
    }
  }

  async loadOlderMessages(): Promise<void> {
    if (!this.selectedConversation || !this.nextBeforeSequence || this.isLoadingMessages) return;
    this.isLoadingMessages = true;
    try {
      const page = await this.api<ConversationDetail>(
        `/conversations/${this.selectedConversation.id}?beforeSequence=${this.nextBeforeSequence}&take=100`);
      this.messages = [...page.messagePage.messages, ...this.messages];
      this.nextBeforeSequence = page.messagePage.nextBeforeSequence ?? null;
    } catch (error) {
      this.error = this.errorMessage(error, 'Unable to load older messages.');
    } finally {
      this.isLoadingMessages = false;
    }
  }

  async deleteConversation(): Promise<void> {
    if (!this.selectedConversation || this.isStreaming) return;
    const conversation = this.selectedConversation;
    if (!confirm(`Delete “${conversation.title}”? This cannot be undone.`)) return;

    try {
      await this.api<void>(`/conversations/${conversation.id}`, { method: 'DELETE' });
      this.conversations = this.conversations.filter(item => item.id !== conversation.id);
      this.selectedConversation = null;
      this.messages = [];
      this.nextBeforeSequence = null;
      if (this.conversations.length) await this.selectConversation(this.conversations[0]);
    } catch (error) {
      this.error = this.errorMessage(error, 'Unable to delete this conversation.');
    }
  }

  async sendMessage(): Promise<void> {
    const prompt = this.prompt.trim();
    if (!prompt || this.isStreaming) return;
    this.error = '';

    if (!this.selectedConversation) {
      await this.createConversation();
      if (!this.selectedConversation) return;
    }

    const conversation = this.selectedConversation;
    const timestamp = new Date().toISOString();
    const localUser: ChatMessage = {
      id: `local-user-${Date.now()}`,
      sequenceNumber: Number.MAX_SAFE_INTEGER - 1,
      role: 'User', status: 'Completed', content: prompt, createdAt: timestamp, updatedAt: timestamp
    };
    const localAssistant: ChatMessage = {
      id: `local-assistant-${Date.now()}`,
      sequenceNumber: Number.MAX_SAFE_INTEGER,
      role: 'Assistant', status: 'Streaming', content: '', createdAt: timestamp, updatedAt: timestamp
    };
    this.messages = [...this.messages, localUser, localAssistant];
    this.prompt = '';
    this.isStreaming = true;
    this.abortController = new AbortController();

    const form = new FormData();
    form.append('prompt', prompt);

    try {
      const response = await fetch(`${this.apiBase}/conversations/${conversation.id}/messages`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${this.token}` },
        body: form,
        signal: this.abortController.signal
      });
      if (!response.ok || !response.body) throw new Error(await this.readError(response));

      await this.readEventStream(response, localUser, localAssistant);
      localAssistant.status = 'Completed';
      this.promoteConversation(conversation.id);
    } catch (error) {
      if ((error as DOMException)?.name === 'AbortError') {
        localAssistant.status = 'Cancelled';
      } else {
        localAssistant.status = 'Failed';
        localAssistant.content ||= this.errorMessage(error, 'The assistant could not complete this response.');
        this.error = localAssistant.content;
      }
    } finally {
      this.isStreaming = false;
      this.abortController = null;
    }
  }

  stopResponse(): void {
    this.abortController?.abort();
  }

  formatMessage(text: string): string {
    return text ? marked.parse(text, { gfm: true, breaks: true }) as string : '';
  }

  formatDate(value?: string | null): string {
    return value ? new Intl.DateTimeFormat([], { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '';
  }

  trackConversation(_: number, conversation: ConversationSummary): string { return conversation.id; }
  trackMessage(_: number, message: ChatMessage): string { return message.id; }

  private async readEventStream(response: Response, user: ChatMessage, assistant: ChatMessage): Promise<void> {
    const reader = response.body!.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const frames = buffer.split('\n\n');
      buffer = frames.pop() ?? '';
      frames.forEach(frame => this.applyStreamFrame(frame, user, assistant));
    }
    buffer += decoder.decode();
    if (buffer.trim()) this.applyStreamFrame(buffer, user, assistant);
  }

  private applyStreamFrame(frame: string, user: ChatMessage, assistant: ChatMessage): void {
    const data = frame.split('\n').find(line => line.startsWith('data: '));
    if (!data) return;
    const event = JSON.parse(data.substring(6)) as StreamEvent;
    if (event.type === 'message-start') {
      if (event.userMessageId) user.id = event.userMessageId;
      if (event.assistantMessageId) assistant.id = event.assistantMessageId;
    } else if (event.type === 'delta') {
      assistant.content += event.text ?? '';
    } else if (event.type === 'completed') {
      assistant.status = 'Completed';
    } else if (event.type === 'error') {
      assistant.status = 'Failed';
      assistant.content ||= event.text ?? 'The assistant could not complete this response.';
    }
  }

  private async api<T>(path: string, init: RequestInit = {}, includeAuthorization = true): Promise<T> {
    const headers = new Headers(init.headers);
    if (includeAuthorization && this.token) headers.set('Authorization', `Bearer ${this.token}`);
    const response = await fetch(`${this.apiBase}${path}`, { ...init, headers });
    if (response.status === 401 && includeAuthorization) this.logout();
    if (!response.ok) throw new Error(await this.readError(response));
    if (response.status === 204) return undefined as T;
    return await response.json() as T;
  }

  private async readError(response: Response): Promise<string> {
    try {
      const body = await response.json() as { detail?: string; title?: string };
      return body.detail ?? body.title ?? `Request failed (${response.status}).`;
    } catch {
      return `Request failed (${response.status}).`;
    }
  }

  private promoteConversation(conversationId: string): void {
    const index = this.conversations.findIndex(item => item.id === conversationId);
    if (index < 0) return;
    const updated = { ...this.conversations[index], updatedAt: new Date().toISOString(), lastMessageAt: new Date().toISOString() };
    this.conversations = [updated, ...this.conversations.filter(item => item.id !== conversationId)];
    this.selectedConversation = updated;
  }

  private toSummary(detail: ConversationDetail): ConversationSummary {
    return {
      id: detail.id,
      title: detail.title,
      createdAt: detail.createdAt,
      updatedAt: detail.updatedAt,
      lastMessageAt: detail.lastMessageAt
    };
  }

  private titleFromPrompt(prompt: string): string {
    const title = prompt.replace(/\s+/g, ' ').trim();
    return title.length <= 80 ? title : `${title.slice(0, 77)}...`;
  }

  private errorMessage(error: unknown, fallback: string): string {
    return error instanceof Error && error.message ? error.message : fallback;
  }
}
