using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using Newtonsoft.Json;

namespace ModelTweakerTool
{
    public class MainForm : Form
    {
        private string _configPath = "";
        private TrackBar? _rotXSlider, _rotYSlider, _rotZSlider, _scaleSlider;
        private TrackBar? _offsetXSlider, _offsetYSlider, _offsetZSlider;
        private Label? _rotXLabel, _rotYLabel, _rotZLabel, _scaleLabel;
        private Label? _offsetXLabel, _offsetYLabel, _offsetZLabel;
        private TextBox? _targetMeshBox;
        private CheckBox? _autoApplyCheck;
        
        public MainForm()
        {
            InitializeComponent();
            SetupUI();
            LoadConfig();
        }
        
        private void InitializeComponent()
        {
            this.Text = "HoboMod Model Tweaker";
            this.Size = new Size(400, 620);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(50, 50);
        }
        
        private void SetupUI()
        {
            int y = 20;
            
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "HoboMod", "tweak_config.json");
            
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null) Directory.CreateDirectory(dir);
            
            // Title
            var titleLabel = new Label { 
                Text = "Model Tweaker - Live Adjustment", 
                Location = new Point(20, y), 
                Size = new Size(350, 25),
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            Controls.Add(titleLabel);
            y += 40;
            
            // Target mesh
            var targetLabel = new Label { Text = "Target Mesh:", Location = new Point(20, y), Size = new Size(80, 20) };
            Controls.Add(targetLabel);
            _targetMeshBox = new TextBox { 
                Text = "HoboCommon_FPS", 
                Location = new Point(110, y), 
                Size = new Size(250, 25) 
            };
            Controls.Add(_targetMeshBox);
            y += 35;
            
            // Rotation X
            (_rotXSlider, _rotXLabel, y) = AddSlider("Rotation X:", -180, 180, 0, y);
            
            // Rotation Y
            (_rotYSlider, _rotYLabel, y) = AddSlider("Rotation Y:", -180, 180, 0, y);
            
            // Rotation Z
            (_rotZSlider, _rotZLabel, y) = AddSlider("Rotation Z:", -180, 180, 0, y);
            
            // Scale
            (_scaleSlider, _scaleLabel, y) = AddSlider("Scale:", 1, 2000, 1000, y, true);
            
            // Offset X/Y/Z (range -500 to 500, represents -5.0 to 5.0 units)
            (_offsetXSlider, _offsetXLabel, y) = AddSlider("Offset X:", -500, 500, 0, y, false, true);
            (_offsetYSlider, _offsetYLabel, y) = AddSlider("Offset Y:", -500, 500, 0, y, false, true);
            (_offsetZSlider, _offsetZLabel, y) = AddSlider("Offset Z:", -500, 500, 0, y, false, true);
            
            y += 10;
            
            // Auto-apply checkbox
            _autoApplyCheck = new CheckBox {
                Text = "Auto-apply on change",
                Location = new Point(20, y),
                Size = new Size(200, 25),
                Checked = true
            };
            Controls.Add(_autoApplyCheck);
            y += 35;
            
            // Buttons
            var applyButton = new Button {
                Text = "Apply Now",
                Location = new Point(20, y),
                Size = new Size(100, 35),
                BackColor = Color.LightGreen
            };
            applyButton.Click += (s, e) => SaveConfig();
            Controls.Add(applyButton);
            
            var resetButton = new Button {
                Text = "Reset All",
                Location = new Point(130, y),
                Size = new Size(100, 35)
            };
            resetButton.Click += (s, e) => ResetValues();
            Controls.Add(resetButton);
            
            var copyButton = new Button {
                Text = "Copy JSON",
                Location = new Point(240, y),
                Size = new Size(100, 35)
            };
            copyButton.Click += (s, e) => CopyJsonToClipboard();
            Controls.Add(copyButton);
            y += 50;
            
            // Status
            var statusLabel = new Label {
                Text = $"Config: {_configPath}",
                Location = new Point(20, y),
                Size = new Size(350, 40),
                Font = new Font("Segoe UI", 8)
            };
            Controls.Add(statusLabel);
            
            // Quick rotation buttons
            y += 45;
            var quickLabel = new Label { Text = "Quick Rotations:", Location = new Point(20, y), Size = new Size(100, 20) };
            Controls.Add(quickLabel);
            y += 25;
            
            AddQuickButton("Stand Up (X-90)", -90, 0, 0, 20, y);
            AddQuickButton("Stand Up (X+90)", 90, 0, 0, 130, y);
            AddQuickButton("Flip (Y180)", 0, 180, 0, 240, y);
            y += 35;
            AddQuickButton("X-90 Y180", -90, 180, 0, 20, y);
            AddQuickButton("X90 Y180", 90, 180, 0, 130, y);
            AddQuickButton("Z90", 0, 0, 90, 240, y);
        }
        
        private (TrackBar slider, Label label, int newY) AddSlider(string labelText, int min, int max, int defaultVal, int y, bool isScale = false, bool isOffset = false)
        {
            var lbl = new Label { Text = labelText, Location = new Point(20, y + 3), Size = new Size(80, 20) };
            Controls.Add(lbl);
            
            var slider = new TrackBar {
                Minimum = min,
                Maximum = max,
                Value = defaultVal,
                Location = new Point(100, y),
                Size = new Size(200, 45),
                TickFrequency = isScale ? 100 : (isOffset ? 50 : 15)
            };
            
            string initText = isScale ? "1.000" : (isOffset ? "0.00" : "0°");
            var valueLabel = new Label { 
                Text = initText, 
                Location = new Point(310, y + 3), 
                Size = new Size(60, 20) 
            };
            Controls.Add(valueLabel);
            
            slider.Scroll += (s, e) => {
                if (isScale)
                    valueLabel.Text = (slider.Value / 1000.0).ToString("F3");
                else if (isOffset)
                    valueLabel.Text = (slider.Value / 100.0).ToString("F2");
                else
                    valueLabel.Text = $"{slider.Value}°";
                    
                if (_autoApplyCheck?.Checked == true)
                    SaveConfig();
            };
            
            Controls.Add(slider);
            return (slider, valueLabel, y + 50);
        }
        
        private void AddQuickButton(string text, int rotX, int rotY, int rotZ, int x, int y)
        {
            var btn = new Button {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(100, 28),
                Font = new Font("Segoe UI", 8)
            };
            btn.Click += (s, e) => {
                if (_rotXSlider != null) _rotXSlider.Value = Math.Max(_rotXSlider.Minimum, Math.Min(_rotXSlider.Maximum, rotX));
                if (_rotYSlider != null) _rotYSlider.Value = Math.Max(_rotYSlider.Minimum, Math.Min(_rotYSlider.Maximum, rotY));
                if (_rotZSlider != null) _rotZSlider.Value = Math.Max(_rotZSlider.Minimum, Math.Min(_rotZSlider.Maximum, rotZ));
                UpdateLabels();
                SaveConfig();
            };
            Controls.Add(btn);
        }
        
        private void UpdateLabels()
        {
            if (_rotXSlider != null && _rotXLabel != null) _rotXLabel.Text = $"{_rotXSlider.Value}°";
            if (_rotYSlider != null && _rotYLabel != null) _rotYLabel.Text = $"{_rotYSlider.Value}°";
            if (_rotZSlider != null && _rotZLabel != null) _rotZLabel.Text = $"{_rotZSlider.Value}°";
            if (_scaleSlider != null && _scaleLabel != null) _scaleLabel.Text = (_scaleSlider.Value / 1000.0).ToString("F3");
            if (_offsetXSlider != null && _offsetXLabel != null) _offsetXLabel.Text = (_offsetXSlider.Value / 100.0).ToString("F2");
            if (_offsetYSlider != null && _offsetYLabel != null) _offsetYLabel.Text = (_offsetYSlider.Value / 100.0).ToString("F2");
            if (_offsetZSlider != null && _offsetZLabel != null) _offsetZLabel.Text = (_offsetZSlider.Value / 100.0).ToString("F2");
        }
        
        private void SaveConfig()
        {
            if (_rotXSlider == null || _rotYSlider == null || _rotZSlider == null || _scaleSlider == null || _targetMeshBox == null)
                return;
                
            var config = new TweakConfig {
                TargetMesh = _targetMeshBox.Text,
                RotX = _rotXSlider.Value,
                RotY = _rotYSlider.Value,
                RotZ = _rotZSlider.Value,
                Scale = _scaleSlider.Value / 1000.0f,
                OffsetX = (_offsetXSlider?.Value ?? 0) / 100.0f,
                OffsetY = (_offsetYSlider?.Value ?? 0) / 100.0f,
                OffsetZ = (_offsetZSlider?.Value ?? 0) / 100.0f,
                Timestamp = DateTime.Now.Ticks
            };
            
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            
            // Retry logic for file access conflicts
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using (var fs = new FileStream(_configPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.Write(json);
                    }
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }
        
        private void LoadConfig()
        {
            if (!File.Exists(_configPath)) return;
            
            try
            {
                string json;
                using (var fs = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }
                
                var config = JsonConvert.DeserializeObject<TweakConfig>(json);
                if (config != null)
                {
                    if (_targetMeshBox != null) _targetMeshBox.Text = config.TargetMesh ?? "HoboCommon_FPS";
                    if (_rotXSlider != null) _rotXSlider.Value = Math.Max(-180, Math.Min(180, config.RotX));
                    if (_rotYSlider != null) _rotYSlider.Value = Math.Max(-180, Math.Min(180, config.RotY));
                    if (_rotZSlider != null) _rotZSlider.Value = Math.Max(-180, Math.Min(180, config.RotZ));
                    if (_scaleSlider != null) _scaleSlider.Value = Math.Max(1, Math.Min(2000, (int)(config.Scale * 1000)));
                    if (_offsetXSlider != null) _offsetXSlider.Value = Math.Max(-500, Math.Min(500, (int)(config.OffsetX * 100)));
                    if (_offsetYSlider != null) _offsetYSlider.Value = Math.Max(-500, Math.Min(500, (int)(config.OffsetY * 100)));
                    if (_offsetZSlider != null) _offsetZSlider.Value = Math.Max(-500, Math.Min(500, (int)(config.OffsetZ * 100)));
                    UpdateLabels();
                }
            }
            catch { }
        }
        
        private void ResetValues()
        {
            if (_rotXSlider != null) _rotXSlider.Value = 0;
            if (_rotYSlider != null) _rotYSlider.Value = 0;
            if (_rotZSlider != null) _rotZSlider.Value = 0;
            if (_scaleSlider != null) _scaleSlider.Value = 1000;
            if (_offsetXSlider != null) _offsetXSlider.Value = 0;
            if (_offsetYSlider != null) _offsetYSlider.Value = 0;
            if (_offsetZSlider != null) _offsetZSlider.Value = 0;
            UpdateLabels();
            SaveConfig();
        }
        
        private void CopyJsonToClipboard()
        {
            var rotX = _rotXSlider?.Value ?? 0;
            var rotY = _rotYSlider?.Value ?? 0;
            var rotZ = _rotZSlider?.Value ?? 0;
            var scale = (_scaleSlider?.Value ?? 1000) / 1000.0;
            var offsetX = (_offsetXSlider?.Value ?? 0) / 100.0;
            var offsetY = (_offsetYSlider?.Value ?? 0) / 100.0;
            var offsetZ = (_offsetZSlider?.Value ?? 0) / 100.0;
            
            var json = $@"{{
    ""rotX"": {rotX},
    ""rotY"": {rotY},
    ""rotZ"": {rotZ},
    ""scale"": {scale:F4},
    ""offsetX"": {offsetX:F2},
    ""offsetY"": {offsetY:F2},
    ""offsetZ"": {offsetZ:F2}
}}";
            Clipboard.SetText(json);
            MessageBox.Show("Copied to clipboard!\n\nPaste into your mod.json modelOverrides section.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    
    public class TweakConfig
    {
        public string TargetMesh { get; set; } = "HoboCommon_FPS";
        public int RotX { get; set; }
        public int RotY { get; set; }
        public int RotZ { get; set; }
        public float Scale { get; set; } = 1.0f;
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float OffsetZ { get; set; }
        public long Timestamp { get; set; }
    }
}
