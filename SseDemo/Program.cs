using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();

var app = builder.Build();

app.UseDefaultFiles();   // mapeia "/" → "index.html"
app.UseStaticFiles();
app.UseCors(x => x
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// ── 1. Relógio ─────────────────────────────────────────────────────────────
// Uso mais simples do .NET 10: TypedResults.ServerSentEvents<T>()
// Aceita IAsyncEnumerable<T> e serializa cada item como JSON automaticamente.
app.MapGet("/sse/clock", (CancellationToken ct) =>
    TypedResults.ServerSentEvents(ClockStream(ct), eventType: "tick"));

// ── 2. Progresso (múltiplos event types) ────────────────────────────────────
// Para emitir event types diferentes por evento, usamos SseFormatter diretamente.
// SseItem<T> + SseFormatter são a camada de baixo nível do protocolo SSE no .NET.
app.MapGet("/sse/progress", async (HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType  = "text/event-stream";
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("X-Accel-Buffering", "no");

    // SseFormatter.WriteAsync recebe IAsyncEnumerable<SseItem<string>>
    // e escreve diretamente no body da resposta, respeitando o protocolo SSE.
    await SseFormatter.WriteAsync(ProgressStream(ct), ctx.Response.Body, ct);
});

// ── 3. Chat — streaming de tokens (estilo LLM) ─────────────────────────────
// Usa SseFormatter.WriteAsync para ter controle total: event type "token" para
// cada palavra e "done" para o sinal de conclusão — sem depender de serialização.
app.MapGet("/sse/chat", async (string? message, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("X-Accel-Buffering", "no");

    await SseFormatter.WriteAsync(ChatStream(message ?? "Olá!", ct), ctx.Response.Body, ct);
});

app.Run();

// ── Streams ────────────────────────────────────────────────────────────────

static async IAsyncEnumerable<ClockPayload> ClockStream(
    [EnumeratorCancellation] CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        yield return new ClockPayload(
            DateTime.Now.ToString("HH:mm:ss"),
            DateTime.Now.ToString("dddd, dd/MM/yyyy"));

        await Task.Delay(1000, ct).ConfigureAwait(false);
    }
}

static async IAsyncEnumerable<SseItem<string>> ProgressStream(
    [EnumeratorCancellation] CancellationToken ct)
{
    var steps = new[]
    {
        "Iniciando conexão...", "Autenticando...", "Baixando configurações...",
        "Processando dados...", "Validando resultados...", "Otimizando saída...",
        "Gerando relatório...", "Empacotando artefatos...", "Enviando notificações...",
        "Finalizando...", "Concluído com sucesso!"
    };

    for (var i = 0; i <= 100; i += 10)
    {
        if (ct.IsCancellationRequested) yield break;

        var payload = JsonSerializer.Serialize(new ProgressPayload(i, steps[i / 10]));
        var isLast  = i == 100;

        // SseItem<string> permite definir eventType por evento individualmente
        yield return new SseItem<string>(payload, eventType: isLast ? "complete" : "progress");

        await Task.Delay(600, ct).ConfigureAwait(false);
    }
}

static async IAsyncEnumerable<SseItem<string>> ChatStream(
    string message,
    [EnumeratorCancellation] CancellationToken ct)
{
    var response =
        $"Recebi: \"{message}\". " +
        "Esta resposta é transmitida palavra por palavra via Server-Sent Events, " +
        "exatamente como modelos de linguagem modernos transmitem texto em tempo real. " +
        "O .NET 10 torna isso nativo com TypedResults.ServerSentEvents — " +
        "sem pacotes externos, sem middleware customizado!";

    foreach (var token in response.Split(' '))
    {
        if (ct.IsCancellationRequested) yield break;
        // eventType "token" — cliente acumula cada palavra na bolha
        yield return new SseItem<string>(token + " ", eventType: "token");
        await Task.Delay(75, ct).ConfigureAwait(false);
    }

    // eventType "done" — cliente sabe que o stream acabou (sem depender de valor especial)
    yield return new SseItem<string>(string.Empty, eventType: "done");
}

// ── Modelos ────────────────────────────────────────────────────────────────
record ClockPayload(string Time, string Date);
record ProgressPayload(int Percent, string Message);
