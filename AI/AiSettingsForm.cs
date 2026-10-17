using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ArdillaUpdater.AI
{
    internal class AiSettingsForm : Form
    {
        private readonly AiConfig _config;

        private TextBox txtEndpoint = null!;
        private ComboBox cmbModel = null!;
        private TextBox txtApiKey = null!;
        private NumericUpDown numTemperature = null!;
        private NumericUpDown numNumCtx = null!;
        private Label lblTestStatus = null!;
        private Button btnTest = null!;
        private Button btnSave = null!;
        private Button btnCancel = null!;

        public AiSettingsForm(AiConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            InitializeComponent();
            LoadConfigValues();
        }

        private void InitializeComponent()
        {
            Text = "Configuración de Inteligencia Artificial";
            Width = 560;
            Height = 520;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9.5F);

            var panelHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(15, 86, 179),
                Padding = new Padding(16, 12, 16, 12)
            };

            var lblTitle = new Label
            {
                Text = "⚙️ Ajustes del Asistente IA",
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 10)
            };

            var lblSubtitle = new Label
            {
                Text = "Configura el servidor Ollama o endpoint compatible para Ardilla Updater",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(220, 235, 255),
                AutoSize = true,
                Location = new Point(16, 36)
            };

            panelHeader.Controls.Add(lblTitle);
            panelHeader.Controls.Add(lblSubtitle);

            var panelBody = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 16, 24, 16)
            };

            // Endpoint
            var lblEndpoint = new Label
            {
                Text = "Servidor / Endpoint (Ollama o API compatible):",
                Location = new Point(24, 16),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };

            txtEndpoint = new TextBox
            {
                Location = new Point(24, 40),
                Width = 490,
                Font = new Font("Segoe UI", 10F)
            };

            // Model
            var lblModel = new Label
            {
                Text = "Modelo (Selecciona de Models.txt o escribe uno personalizado):",
                Location = new Point(24, 80),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };

            cmbModel = new ComboBox
            {
                Location = new Point(24, 104),
                Width = 490,
                Font = new Font("Segoe UI", 10F),
                DropDownStyle = ComboBoxStyle.DropDown
            };

            foreach (var m in AiConfig.GetAvailableModels())
            {
                cmbModel.Items.Add(m);
            }

            // Api Key
            var lblApiKey = new Label
            {
                Text = "API Key / Token (Opcional si usas Ollama local):",
                Location = new Point(24, 144),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };

            txtApiKey = new TextBox
            {
                Location = new Point(24, 168),
                Width = 490,
                PasswordChar = '•',
                Font = new Font("Segoe UI", 10F)
            };

            // Temperature
            var lblTemperature = new Label
            {
                Text = "Temperatura (0.1 - 1.0):",
                Location = new Point(24, 210),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };

            numTemperature = new NumericUpDown
            {
                Location = new Point(24, 234),
                Width = 150,
                Minimum = 0.1m,
                Maximum = 1.0m,
                DecimalPlaces = 2,
                Increment = 0.05m,
                Font = new Font("Segoe UI", 10F)
            };

            // NumCtx
            var lblNumCtx = new Label
            {
                Text = "Contexto (num_ctx):",
                Location = new Point(200, 210),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };

            numNumCtx = new NumericUpDown
            {
                Location = new Point(200, 234),
                Width = 150,
                Minimum = 1024,
                Maximum = 131072,
                Increment = 1024,
                Font = new Font("Segoe UI", 10F)
            };

            // Test connection
            btnTest = new Button
            {
                Text = "🔍 Probar Conexión",
                Location = new Point(24, 275),
                Width = 150,
                Height = 36,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnTest.FlatAppearance.BorderColor = Color.FromArgb(200, 210, 220);
            btnTest.Click += async (_, __) => await TestConnectionAsync();

            lblTestStatus = new Label
            {
                Text = "",
                Location = new Point(184, 283),
                AutoSize = true,
                ForeColor = Color.FromArgb(70, 80, 90)
            };

            // Botones de acción
            btnSave = new Button
            {
                Text = "Guardar",
                Location = new Point(290, 340),
                Width = 110,
                Height = 38,
                BackColor = Color.FromArgb(15, 86, 179),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (_, __) => SaveAndClose();

            btnCancel = new Button
            {
                Text = "Cancelar",
                Location = new Point(410, 340),
                Width = 104,
                Height = 38,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(200, 210, 220);
            btnCancel.Click += (_, __) => Close();

            panelBody.Controls.Add(lblEndpoint);
            panelBody.Controls.Add(txtEndpoint);
            panelBody.Controls.Add(lblModel);
            panelBody.Controls.Add(cmbModel);
            panelBody.Controls.Add(lblApiKey);
            panelBody.Controls.Add(txtApiKey);
            panelBody.Controls.Add(lblTemperature);
            panelBody.Controls.Add(numTemperature);
            panelBody.Controls.Add(lblNumCtx);
            panelBody.Controls.Add(numNumCtx);
            panelBody.Controls.Add(btnTest);
            panelBody.Controls.Add(lblTestStatus);
            panelBody.Controls.Add(btnSave);
            panelBody.Controls.Add(btnCancel);

            Controls.Add(panelBody);
            Controls.Add(panelHeader);
        }

        private void LoadConfigValues()
        {
            txtEndpoint.Text = _config.Endpoint;
            cmbModel.Text = _config.Model;
            txtApiKey.Text = _config.ApiKey;
            numTemperature.Value = (decimal)Math.Clamp(_config.Temperature, 0.1, 1.0);
            numNumCtx.Value = (decimal)Math.Clamp(_config.NumCtx > 0 ? _config.NumCtx : 8192, numNumCtx.Minimum, numNumCtx.Maximum);
        }

        private async Task TestConnectionAsync()
        {
            string endpoint = txtEndpoint.Text.Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                lblTestStatus.Text = "❌ Ingresa una URL.";
                lblTestStatus.ForeColor = Color.Red;
                return;
            }

            string model = cmbModel.Text.Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                lblTestStatus.Text = "❌ Selecciona o escribe un modelo.";
                lblTestStatus.ForeColor = Color.Red;
                return;
            }

            btnTest.Enabled = false;
            lblTestStatus.Text = "Probando conexión...";
            lblTestStatus.ForeColor = Color.FromArgb(70, 80, 90);

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                if (!string.IsNullOrWhiteSpace(txtApiKey.Text))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", txtApiKey.Text.Trim());
                }

                var testUrl = endpoint.TrimEnd('/') + "/api/tags";
                var response = await client.GetAsync(testUrl);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var root = JObject.Parse(json);
                        var models = root["models"] as JArray;
                        bool found = false;
                        if (models != null)
                        {
                            foreach (var m in models)
                            {
                                var name = m["name"]?.ToString() ?? m["model"]?.ToString() ?? string.Empty;
                                if (string.Equals(name, model, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(name.Split(':')[0], model.Split(':')[0], StringComparison.OrdinalIgnoreCase))
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (found)
                        {
                            lblTestStatus.Text = "✅ Conexión correcta y modelo detectado en /api/tags.";
                            lblTestStatus.ForeColor = Color.DarkGreen;
                        }
                        else
                        {
                            lblTestStatus.Text = "⚠️ Conexión OK, pero el modelo no aparece en /api/tags (puede necesitar descarga o login).";
                            lblTestStatus.ForeColor = Color.FromArgb(180, 100, 0);
                        }
                    }
                    catch
                    {
                        lblTestStatus.Text = "✅ ¡Conexión con el servidor exitosa!";
                        lblTestStatus.ForeColor = Color.DarkGreen;
                    }
                }
                else
                {
                    lblTestStatus.Text = $"⚠️ Servidor respondió con código {(int)response.StatusCode}.";
                    lblTestStatus.ForeColor = Color.FromArgb(180, 100, 0);
                }
            }
            catch (Exception ex)
            {
                lblTestStatus.Text = $"❌ No conecta: {ex.Message}";
                lblTestStatus.ForeColor = Color.Red;
            }
            finally
            {
                btnTest.Enabled = true;
            }
        }

        private void SaveAndClose()
        {
            _config.Endpoint = txtEndpoint.Text.Trim();
            _config.Model = cmbModel.Text.Trim();
            _config.ApiKey = txtApiKey.Text.Trim();
            _config.Temperature = (double)numTemperature.Value;
            _config.NumCtx = (int)numNumCtx.Value;
            _config.Save();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
