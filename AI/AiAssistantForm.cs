using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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
        private Label lblModelBadge = null!;
        private Label lblLiveStatus = null!;
        private FlowLayoutPanel flowChips = null!;

        private CancellationTokenSource? _currentCts;
        private volatile bool _closing;

        internal AiAssistantForm(Form1 mainForm, DlcCatalogService dlcCatalog)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _config = AiConfig.Load();
            _aiService = new ArdillaAiService(_config);

            _agent = new ArdillaAiAgent(
                _aiService,
                () => _mainForm.GamePath,
                dlcCatalog,
                OnPacksSelectedByAi
            );

            InitializeComponent();
            AppendWelcomeMessage();
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

            var panelTopRight = new Panel
            {
                Dock = DockStyle.Right,
                Width = 230,
                BackColor = Color.Transparent
            };

            btnSettings = new Button
            {
                Text = "⚙️ Ajustes",
                Width = 98,
                Height = 36,
                Location = new Point(12, 18),
                BackColor = Color.FromArgb(30, 110, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };
            btnSettings.FlatAppearance.BorderSize = 0;
            btnSettings.Click += (_, __) => OpenSettings();

            btnClear = new Button
            {
                Text = "🧹 Limpiar",
                Width = 96,
                Height = 36,
                Location = new Point(118, 18),
                BackColor = Color.FromArgb(30, 110, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };
            btnClear.FlatAppearance.BorderSize = 0;
            btnClear.Click += (_, __) => ClearChat();

            panelTopRight.Controls.Add(btnSettings);
            panelTopRight.Controls.Add(btnClear);

            panelTop.Controls.Add(panelTopRight);
            panelTop.Controls.Add(lblTitle);
            panelTop.Controls.Add(lblModelBadge);

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
                Height = 110,
                BackColor = Color.White,
                Padding = new Padding(16, 8, 16, 12)
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
                Width = 140,
                Padding = new Padding(8, 0, 0, 0)
            };

            btnSend = new Button
            {
                Text = "➤ Enviar",
                Dock = DockStyle.Top,
                Height = 44,
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
                Height = 30,
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
                    null,
                    ct
                );

                AppendMessage("Ardilla AI", response, Color.FromArgb(15, 86, 179));
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

            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;

            rtbChat.SelectionFont = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            rtbChat.SelectionColor = senderColor;
            rtbChat.AppendText($"\n[{sender}] - {DateTime.Now:HH:mm}\n");

            rtbChat.SelectionFont = new Font("Segoe UI", 9.8F, FontStyle.Regular);
            rtbChat.SelectionColor = Color.FromArgb(30, 35, 42);
            rtbChat.AppendText(text + "\n");

            rtbChat.ScrollToCaret();
        }

        private void ClearChat()
        {
            _aiService.ClearHistory();
            rtbChat.Clear();
            AppendWelcomeMessage();
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
