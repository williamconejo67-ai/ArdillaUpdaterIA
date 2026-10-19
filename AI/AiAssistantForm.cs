using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ArdillaUpdater.AI
{
    internal class AiAssistantForm : Form
    {
        private readonly AiConfig _config;
        private readonly ArdillaAiService _aiService;
        private readonly ArdillaAiAgent _agent;
        private readonly Form1 _mainForm;

        private RichTextBox rtbChat = null!;
        private TextBox txtInput = null!;
        private Button btnSend = null!;
        private Button btnCancel = null!;
        private Button btnSettings = null!;
        private Button btnClear = null!;
        private Button btnCopy = null!;
        private Button btnExport = null!;
        private Label lblModelBadge = null!;
        private Label lblLiveStatus = null!;
        private FlowLayoutPanel flowChips = null!;

        private CancellationTokenSource? _currentCts;
        private volatile bool _closing;

        // Streaming: guarda el texto del bloque actual para no duplicar la respuesta final.
        private readonly StringBuilder _streamBuffer = new();
        private bool _streamSegmentStarted;

        // Fuentes reutilizadas (se crean una sola vez, no en cada mensaje).
        private readonly Font _headerFont = new("Segoe UI Semibold", 10.5F, FontStyle.Bold);
        private readonly Font _bodyFont = new("Segoe UI", 9.8F, FontStyle.Regular);

        // Log de la ventana principal: sirve para avisar si guardar o cargar la
        // configuración / conversación falla (antes todos los fallos eran silenciosos).
        private readonly Action<string>? _log;

        internal AiAssistantForm(Form1 mainForm, DlcCatalogService dlcCatalog, Action<string>? log = null)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _log = log;
            _config = AiConfig.Load(log: _log);
            _aiService = new ArdillaAiService(_config);

            _agent = new ArdillaAiAgent(
                _aiService,
                () => _mainForm.GamePath,
                dlcCatalog,
                OnPacksSelectedByAi
            );

            InitializeComponent();
            RestoreConversation();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Marcar el cierre antes de liberar: las continuaciones de
                // SendMessageAsync ya no tocarán controles destruidos.
                _closing = true;
                DisposeCurrentCts(cancel: true);
                _aiService.Dispose();
            }

            base.Dispose(disposing);

            if (disposing)
            {
                _headerFont.Dispose();
                _bodyFont.Dispose();
            }
        }

        /// <summary>
        /// Libera el CancellationTokenSource actual en un único punto. El intercambio
        /// atómico garantiza que, aunque Dispose y una continuación asíncrona se
        /// solapen, solo una de las dos rutas obtiene el CTS y lo libera.
        /// </summary>
        private void DisposeCurrentCts(bool cancel)
        {
            var cts = Interlocked.Exchange(ref _currentCts, null);
            if (cts == null)
            {
                return;
            }

            if (cancel)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ya estaba liberado: nada que cancelar.
                }
            }

            cts.Dispose();
        }

        private void InitializeComponent()
        {
            Text = "Ardilla AI - Asistente Inteligente";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Width = 960;
            Height = 700;
            MinimumSize = new Size(800, 550);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9.5F);

            // ================= HEADER =================
            var panelTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.FromArgb(15, 86, 179),
                Padding = new Padding(18, 10, 18, 10)
            };

            var lblTitle = new Label
            {
                Text = "🤖 Ardilla AI Copilot",
                Font = new Font("Segoe UI Semibold", 13.5F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(18, 10)
            };

            lblModelBadge = new Label
            {
                Text = $"Modelo: {_config.Model}  |  {_config.Endpoint}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(215, 235, 255),
                AutoSize = true,
                Location = new Point(20, 42)
            };

            // Panel de botones superiores: SIN Dock (el Dock + AutoSize rompía el ancho y los
            // botones se envolvían a una fila que quedaba recortada por la cabecera fija).
            // Se posiciona a mano con el evento Layout: una sola fila, centrado verticalmente
            // y sin pisar el título a cualquier escala de DPI.
            var panelTopRight = new FlowLayoutPanel
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4, 4, 4, 4),
                Margin = new Padding(0)
            };

            btnSettings = new Button
            {
                Text = "⚙️ Ajustes",
                AutoSize = true,
                BackColor = Color.FromArgb(30, 110, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Margin = new Padding(2, 2, 2, 2)
            };
            btnSettings.FlatAppearance.BorderSize = 0;
            btnSettings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnSettings.Click += (_, __) => OpenSettings();

            btnCopy = new Button
            {
                Text = "📋 Copiar última respuesta",
                AutoSize = true,
                BackColor = Color.FromArgb(30, 110, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Margin = new Padding(2, 2, 2, 2)
            };
            btnCopy.FlatAppearance.BorderSize = 0;
            btnCopy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnCopy.Click += (_, __) => CopyLastAssistantResponse();

            btnExport = new Button
            {
                Text = "💾 Exportar chat",
                AutoSize = true,
                BackColor = Color.FromArgb(30, 110, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Margin = new Padding(2, 2, 2, 2)
            };
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnExport.Click += (_, __) => ExportChat();

            btnClear = new Button
            {
                Text = "🧹 Limpiar",
                AutoSize = true,
                BackColor = Color.FromArgb(30, 110, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Margin = new Padding(2, 2, 2, 2)
            };
            btnClear.FlatAppearance.BorderSize = 0;
            btnClear.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnClear.Click += (_, __) => ClearChat();

            panelTopRight.Controls.Add(btnSettings);
            panelTopRight.Controls.Add(btnCopy);
            panelTopRight.Controls.Add(btnExport);
            panelTopRight.Controls.Add(btnClear);

            panelTop.Controls.Add(panelTopRight);
            panelTop.Controls.Add(lblTitle);
            panelTop.Controls.Add(lblModelBadge);

            // Coloca los botones en una sola fila a la derecha, centrados verticalmente en la
            // cabecera. Si no cabe junto al título, se desplaza hacia el centro (izquierda)
            // para que nunca queden ocultos ni recortados, a cualquier escala de DPI.
            void PosicionarBotonesCabecera()
            {
                if (panelTopRight.Width == 0 || panelTopRight.Height == 0)
                {
                    return;
                }

                int margenDerecho = 18;
                // El botón nunca debe pisar ni el título ni el badge del modelo (el que resulte más ancho).
                int margenTitulo = Math.Max(12, Math.Max(lblTitle.Right, lblModelBadge.Right) + 12);
                int x = panelTop.ClientSize.Width - panelTopRight.Width - margenDerecho;
                if (x < margenTitulo)
                {
                    x = margenTitulo;
                }

                int y = Math.Max(2, (panelTop.ClientSize.Height - panelTopRight.Height) / 2);
                var nuevaPos = new Point(x, y);
                if (panelTopRight.Location != nuevaPos)
                {
                    panelTopRight.Location = nuevaPos;
                }
            }

            panelTop.Layout += (_, __) => PosicionarBotonesCabecera();
            panelTopRight.Layout += (_, __) => PosicionarBotonesCabecera();
            PosicionarBotonesCabecera();

            // ================= QUICK CHIPS =================
            flowChips = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(238, 244, 250),
                Padding = new Padding(12, 6, 12, 6),
                WrapContents = false,
                AutoScroll = true
            };

            AddActionButton("⚙️ Ajustes IA", () => OpenSettings());
            AddQuickChip("🔍 ¿Qué DLCs me faltan?", "Revisa qué DLCs y expansiones tengo instalados en mi juego y dime cuáles me faltan.");
            AddQuickChip("⚠️ Analizar errores (lastException)", "Diagnostica si tengo errores en lastException.txt o problemas con mods.");
            AddQuickChip("💡 Recomendar packs", "¿Cuáles son las expansiones más recomendadas y qué novedades traen?");
            AddQuickChip("💾 Espacio en disco", "¿Tengo suficiente espacio libre en el disco para instalar los DLCs?");
            AddQuickChip("🛡️ Verificar archivos", "Verifica la integridad de los archivos del juego en la carpeta de instalación.");

            // ================= BOTTOM INPUT =================
            var panelBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                // Altura suficiente para que Enviar + Detener quepan SIN solaparse
                // (84 px de área no alcanzaban para 54 + 42; ahora hay 104 px).
                Height = 170,
                BackColor = Color.White,
                Padding = new Padding(16, 10, 16, 14)
            };

            lblLiveStatus = new Label
            {
                Text = "Listo para ayudarte.",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 115, 130)
            };

            var panelInputBox = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 4, 0, 0)
            };

            txtInput = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                ScrollBars = ScrollBars.Vertical
            };
            txtInput.KeyDown += TxtInput_KeyDown;

            var panelButtons = new Panel
            {
                Dock = DockStyle.Right,
                Width = 160,
                Padding = new Padding(8, 8, 0, 8)
            };

            btnSend = new Button
            {
                Text = "➤ Enviar",
                // Dock Top: la separación con Detener la da el espacio libre del panel,
                // no Margin (Dock ignora Margin en WinForms).
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = Color.FromArgb(15, 86, 179),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Click += async (_, __) => await SendMessageAsync();

            btnCancel = new Button
            {
                Text = "Detener",
                Dock = DockStyle.Bottom,
                Height = 42,
                BackColor = Color.FromArgb(240, 240, 240),
                ForeColor = Color.FromArgb(160, 40, 40),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (_, __) => CancelCurrentRequest();

            panelButtons.Controls.Add(btnSend);
            panelButtons.Controls.Add(btnCancel);

            panelInputBox.Controls.Add(txtInput);
            panelInputBox.Controls.Add(panelButtons);

            panelBottom.Controls.Add(panelInputBox);
            panelBottom.Controls.Add(lblLiveStatus);

            // ================= CHAT AREA =================
            var panelCenter = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 12, 16, 8)
            };

            rtbChat = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F),
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            panelCenter.Controls.Add(rtbChat);

            Controls.Add(panelCenter);
            Controls.Add(panelBottom);
            Controls.Add(flowChips);
            Controls.Add(panelTop);
        }

        private void AddActionButton(string text, Action action)
        {
            var btnAction = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 32,
                BackColor = Color.FromArgb(230, 242, 255),
                ForeColor = Color.FromArgb(15, 86, 179),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 8, 0)
            };
            btnAction.FlatAppearance.BorderColor = Color.FromArgb(160, 205, 250);
            btnAction.Click += (_, __) => action();
            flowChips.Controls.Add(btnAction);
        }

        private void AddQuickChip(string text, string prompt)
        {
            var btnChip = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 32,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 0, 8, 0)
            };
            btnChip.FlatAppearance.BorderColor = Color.FromArgb(200, 215, 230);
            btnChip.Click += async (_, __) =>
            {
                txtInput.Text = prompt;
                await SendMessageAsync();
            };
            flowChips.Controls.Add(btnChip);
        }

        private void AppendWelcomeMessage()
        {
            AppendMessage(
                "Ardilla AI",
                "¡Hola! Soy tu asistente de inteligencia artificial integrado en Ardilla Updater.\n\n" +
                "Puedo ayudarte a:\n" +
                "• 🔍 Comprobar qué DLCs tienes y cuáles te faltan por descargar.\n" +
                "• ⚠️ Diagnosticar errores de 'lastException.txt' y problemas con tus mods.\n" +
                "• 💡 Recomendarte los mejores packs y preparar su descarga.\n" +
                "• 💾 Revisar si tienes espacio suficiente en disco.\n\n" +
                "Escribe tu pregunta abajo o pulsa cualquiera de los botones rápidos para comenzar.",
                Color.FromArgb(15, 86, 179)
            );
        }

        private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                _ = SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            string message = txtInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(message) || btnSend.Enabled == false) return;

            txtInput.Clear();
            SetUiBusy(true);

            AppendMessage("Tú", message, Color.FromArgb(40, 50, 60));

            _currentCts = new CancellationTokenSource();
            var ct = _currentCts.Token;

            try
            {
                string response = await _agent.ProcessUserMessageAsync(
                    message,
                    status => SetStatus(status),
                    AppendAssistantChunk,
                    ct,
                    BeginAssistantMessage
                );

                // Si el último bloque no recibió texto por streaming (por ejemplo, una
                // respuesta que solo usó herramientas), se pinta la respuesta completa.
                if (!WasShownByStreaming(response))
                {
                    AppendMessage("Ardilla AI", response, Color.FromArgb(15, 86, 179));
                }
                SetStatus("Listo.");
            }
            catch (OperationCanceledException)
            {
                AppendMessage("Sistema", "🛑 Solicitud cancelada.", Color.FromArgb(180, 50, 50));
                SetStatus("Cancelado.");
            }
            catch (Exception ex)
            {
                AppendMessage("Error", $"Ocurrió un problema: {ex.Message}", Color.Red);
                SetStatus("Error en la solicitud.");
            }
            finally
            {
                DisposeCurrentCts(cancel: false);

                // Se guarda el historial para no perder la conversación al cerrar la ventana.
                AiConversationStore.Save(_aiService.GetHistory(), log: _log);

                if (!_closing)
                {
                    SetUiBusy(false);
                    txtInput.Focus();
                }
            }
        }

        private void CancelCurrentRequest()
        {
            try
            {
                _currentCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // La solicitud ya terminó y el CTS fue liberado.
            }
        }

        private void SetUiBusy(bool busy)
        {
            btnSend.Enabled = !busy;
            btnCancel.Enabled = busy;
            flowChips.Enabled = !busy;
            btnClear.Enabled = !busy;
            btnCopy.Enabled = !busy;
            btnExport.Enabled = !busy;
        }

        private void SetStatus(string status)
        {
            if (_closing || IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => SetStatus(status)));
                }
                catch (ObjectDisposedException)
                {
                    // El formulario se cerró mientras llegaba la actualización.
                }
                catch (InvalidOperationException)
                {
                    // El handle ya no existe (formulario cerrado).
                }
                return;
            }

            lblLiveStatus.Text = status;
        }

        private void AppendMessage(string sender, string text, Color senderColor)
        {
            if (_closing || IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => AppendMessage(sender, text, senderColor)));
                }
                catch (ObjectDisposedException)
                {
                    // El formulario se cerró mientras llegaba el mensaje.
                }
                catch (InvalidOperationException)
                {
                    // El handle ya no existe (formulario cerrado).
                }
                return;
            }

            AppendHeader(sender, senderColor);
            AppendRawText(text + "\n");
            rtbChat.ScrollToCaret();
        }

        /// <summary>
        /// Escribe la cabecera de un mensaje ("[Quién] - hora"). Debe llamarse en el hilo de UI.
        /// </summary>
        private void AppendHeader(string sender, Color senderColor)
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionFont = _headerFont;
            rtbChat.SelectionColor = senderColor;
            rtbChat.AppendText($"\n[{sender}] - {DateTime.Now:HH:mm}\n");
        }

        /// <summary>
        /// Escribe texto del cuerpo de un mensaje. Debe llamarse en el hilo de UI.
        /// </summary>
        private void AppendRawText(string text)
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectionFont = _bodyFont;
            rtbChat.SelectionColor = Color.FromArgb(30, 35, 42);
            rtbChat.AppendText(text);
        }

        /// <summary>
        /// Avisa de que empieza un mensaje nuevo del asistente (una vuelta del agente).
        /// Se limpiaba el buffer para saber si la respuesta final ya se mostró por streaming.
        /// </summary>
        private void BeginAssistantMessage()
        {
            if (_closing || IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(BeginAssistantMessage));
                }
                catch (ObjectDisposedException)
                {
                    // El formulario se cerró mientras llegaba el aviso.
                }
                catch (InvalidOperationException)
                {
                    // El handle ya no existe (formulario cerrado).
                }
                return;
            }

            _streamSegmentStarted = false;
            _streamBuffer.Clear();
            SetStatus("✍️ Ardilla AI está respondiendo...");
        }

        /// <summary>
        /// Recibe un fragmento de texto del modelo y lo va escribiendo en el chat.
        /// Se ejecuta en un hilo de fondo, por eso se envía al hilo de UI.
        /// </summary>
        private void AppendAssistantChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk) || _closing || IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => AppendAssistantChunk(chunk)));
                }
                catch (ObjectDisposedException)
                {
                    // El formulario se cerró durante el streaming.
                }
                catch (InvalidOperationException)
                {
                    // El handle ya no existe (formulario cerrado).
                }
                return;
            }

            if (!_streamSegmentStarted)
            {
                AppendHeader("Ardilla AI", Color.FromArgb(15, 86, 179));
                _streamSegmentStarted = true;
            }

            _streamBuffer.Append(chunk);
            AppendRawText(chunk);
            rtbChat.ScrollToCaret();
        }

        /// <summary>
        /// Indica si la respuesta final ya quedó escrita por el streaming, para no duplicarla.
        /// Debe llamarse en el hilo de UI (justo después del await).
        /// </summary>
        private bool WasShownByStreaming(string response)
        {
            if (_streamBuffer.Length == 0)
            {
                return false;
            }

            return string.Equals(_streamBuffer.ToString().Trim(), response.Trim(), StringComparison.Ordinal);
        }

        private void ClearChat()
        {
            _aiService.ClearHistory();
            AiConversationStore.Clear();
            rtbChat.Clear();
            AppendWelcomeMessage();
            SetStatus("Listo para ayudarte.");
        }

        private void CopyLastAssistantResponse()
        {
            try
            {
                // Buscar la última respuesta del asistente en el RichTextBox.
                string text = rtbChat.Text;
                int lastHeader = text.LastIndexOf("[Ardilla AI] -", StringComparison.Ordinal);
                if (lastHeader < 0)
                {
                    MessageBox.Show(this, "No hay ninguna respuesta de Ardilla AI para copiar.", "Copiar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int start = text.IndexOf("\n", lastHeader, StringComparison.Ordinal);
                if (start < 0)
                {
                    start = lastHeader;
                }
                else
                {
                    start++;
                }

                int end = text.IndexOf("\n[", start, StringComparison.Ordinal);
                if (end < 0)
                {
                    end = text.Length;
                }

                string content = text.Substring(start, end - start).Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    MessageBox.Show(this, "La última respuesta está vacía.", "Copiar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Clipboard.SetText(content);
                SetStatus("Última respuesta copiada al portapapeles.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"No se pudo copiar: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Exporta la conversación completa a un archivo de texto (.txt) elegido por el usuario.
        /// </summary>
        private void ExportChat()
        {
            try
            {
                string text = rtbChat.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show(this, "No hay ninguna conversación para exportar.", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using var saveDialog = new SaveFileDialog
                {
                    Title = "Exportar conversación de Ardilla AI",
                    Filter = "Archivo de texto (*.txt)|*.txt|Markdown (*.md)|*.md",
                    FileName = $"ardilla_ai_chat_{DateTime.Now:yyyyMMdd_HHmm}.txt",
                    DefaultExt = "txt",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (saveDialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                File.WriteAllText(saveDialog.FileName, text, Encoding.UTF8);
                SetStatus($"Conversación exportada a: {saveDialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"No se pudo exportar la conversación: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Al abrir la ventana, recupera la conversación guardada en disco. Si no hay nada
        /// guardado, muestra el mensaje de bienvenida.
        /// </summary>
        private void RestoreConversation()
        {
            var history = AiConversationStore.Load(log: _log);
            if (history.Count == 0)
            {
                AppendWelcomeMessage();
                return;
            }

            _aiService.ReplaceHistory(history);

            AppendMessage(
                "Sistema",
                "Conversación anterior restaurada. El asistente recuerda el contexto; pulsa 🧹 Limpiar para empezar de cero.",
                Color.FromArgb(120, 120, 120)
            );

            foreach (var message in history)
            {
                string role = ArdillaAiService.GetRole(message);
                string content = GetMessageContent(message);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (role == "system")
                {
                    // Ocultar notas técnicas internas al restaurar la conversación.
                    if (content.IndexOf("[NOTA TÉCNICA", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }
                    continue;
                }

                if (role == "user")
                {
                    AppendMessage("Tú", content, Color.FromArgb(40, 50, 60));
                }
                else if (role == "assistant")
                {
                    AppendMessage("Ardilla AI", content, Color.FromArgb(15, 86, 179));
                }
            }
        }

        private static string GetMessageContent(object message)
        {
            try
            {
                var token = message as JToken ?? JToken.FromObject(message);
                return token["content"]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void OpenSettings()
        {
            using var dlg = new AiSettingsForm(_config);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                lblModelBadge.Text = $"Modelo: {_config.Model}  |  {_config.Endpoint}";
            }
        }

        private void OnPacksSelectedByAi(List<string> packIds)
        {
            if (_closing || IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => OnPacksSelectedByAi(packIds)));
                }
                catch (ObjectDisposedException)
                {
                    // El formulario se cerró antes de poder notificar.
                }
                catch (InvalidOperationException)
                {
                    // El handle ya no existe (formulario cerrado).
                }
                return;
            }

            // Notificar al usuario y darle la opción de abrir la ventana selectiva
            var res = MessageBox.Show(
                this,
                $"Ardilla AI ha identificado {packIds.Count} packs:\n{string.Join(", ", packIds.Take(15))}" +
                (packIds.Count > 15 ? "..." : "") +
                "\n\n¿Deseas abrir la descarga selectiva ahora para revisarlos e instalarlos?",
                "Packs Seleccionados por IA",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (res == DialogResult.Yes)
            {
                _mainForm.OpenSelectiveDownloadWithFilter(packIds);
            }
        }
    }
}
