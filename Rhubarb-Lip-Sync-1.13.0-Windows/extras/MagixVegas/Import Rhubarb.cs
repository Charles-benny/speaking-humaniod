using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.UI.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using System.Xml;
using System.Xml.Serialization;
using ScriptPortal.Vegas; // For older versions, this should say Sony.Vegas

public class EntryPoint {
	public void FromVegas(Vegas vegas) {
		Config config = Config.Load();
		ImportDialog importDialog = new ImportDialog(config, delegate { Import(config, vegas); });
		importDialog.ShowDialog();
		config.Save();
	}

	private void Import(Config config, Vegas vegas) {
		// Load XML file
		if (!File.Exists(config.XmlFile)) {
			throw new Exception("XML file does not exist.");
		}
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.Load(config.XmlFile);

		// Determine image file names
		XmlNodeList mouthCueElements = xmlDocument.SelectNodes("//mouthCue");
		List<string> shapeNames = new List<string>();
		foreach (XmlElement mouthCueElement in mouthCueElements) {
			if (!shapeNames.Contains(mouthCueElement.InnerText)) {
				shapeNames.Add(mouthCueElement.InnerText);
			}
		}
		Dictionary<string, string> imageFileNames = GetImageFileNames(config.OneImageFile, shapeNames.ToArray());

		// Create new project
		bool promptSave = !config.DiscardChanges;
		bool showDialog = false;
		Project project = new Project(promptSave, showDialog);

		// Set frame size
		Bitmap testImage = new Bitmap(config.OneImageFile);
		project.Video.Width = testImage.Width;
		project.Video.Height = testImage.Height;

		// Set frame rate
		if (config.FrameRate < 0.1 || config.FrameRate > 100) {
			throw new Exception("Invalid frame rate.");
		}
		project.Video.FrameRate = config.FrameRate;

		// Set other video settings
		project.Video.FieldOrder = VideoFieldOrder.ProgressiveScan;
		project.Video.PixelAspectRatio = 1;

		// Add video track with images
		VideoTrack videoTrack = vegas.Project.AddVideoTrack();
		foreach (XmlElement mouthCueElement in mouthCueElements) {
			Timecode start = GetTimecode(mouthCueElement.Attributes["start"]);
			Timecode length = GetTimecode(mouthCueElement.Attributes["end"]) - start;
			VideoEvent videoEvent = videoTrack.AddVideoEvent(start, length);
			Media imageMedia = new Media(imageFileNames[mouthCueElement.InnerText]);
			videoEvent.AddTake(imageMedia.GetVideoStreamByIndex(0));
		}

		// Add audio track with original sound file
		AudioTrack audioTrack = vegas.Project.AddAudioTrack();
		Media audioMedia = new Media(xmlDocument.SelectSingleNode("//soundFile").InnerText);
		AudioEvent audioEvent = audioTrack.AddAudioEvent(new Timecode(0), audioMedia.Length);
		audioEvent.AddTake(audioMedia.GetAudioStreamByIndex(0));
	}

	private static Timecode GetTimecode(XmlAttribute valueAttribute) {
		double seconds = Double.Parse(valueAttribute.Value, CultureInfo.InvariantCulture);
		return Timecode.FromSeconds(seconds);
	}

	private Dictionary<string, string> GetImageFileNames(string oneImageFile, string[] shapeNames) {
		if (oneImageFile == null) {
			throw new Exception("Image file name not set.");
		}
		Regex nameRegex = new Regex(@"(?<=-)([^-]*)(?=\.[^.]+$)");
		if (!nameRegex.IsMatch(oneImageFile)) {
			throw new Exception("Image file name doesn't have expected format.");
		}

		Dictionary<string, string> result = new Dictionary<string, string>();
		foreach (string shapeName in shapeNames) {
			string imageFileName = nameRegex.Replace(oneImageFile, shapeName);
			if (!File.Exists(imageFileName)) {
				throw new Exception(string.Format("Image file '{0}' not found.", imageFileName));
			}
			result[shapeName] = imageFileName;
		}
		return result;
	}

}

public class Config {

	private string xmlFile;
	private string oneImageFile;
	private double frameRate = 100;
	private bool discardChanges = false;

	[DisplayName("XML File")]
	[Description("An XML file generated by Rhubarb Lip Sync.")]
	[Editor(typeof(XmlFileEditor), typeof(UITypeEditor))]
	public string XmlFile {
		get { return xmlFile; }
		set { xmlFile = value; }
	}

	[DisplayName("One image file")]
	[Description("Any image file out of the set of image files representing the mouth chart.")]
	[Editor(typeof(FileNameEditor), typeof(UITypeEditor))]
	public string OneImageFile {
		get { return oneImageFile; }
		set { oneImageFile = value; }
	}

	[DisplayName("Frame rate")]
	[Description("The frame rate for the new project.")]
	public double FrameRate {
		get { return frameRate; }
		set { frameRate = value; }
	}

	[DisplayName("Discard Changes")]
	[Description("Discard all changes to the current project without prompting to save.")]
	public bool DiscardChanges {
		get { return discardChanges; }
		set { discardChanges = value; }
	}

	private static string ConfigFileName {
		get {
			string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return Path.Combine(folder, "ImportRhubarbSettings.xml");
		}
	}

	public static Config Load() {
		try {
			XmlSerializer serializer = new XmlSerializer(typeof(Config));
			using (FileStream file = File.OpenRead(ConfigFileName)) {
				return (Config) serializer.Deserialize(file);
			}
		} catch (Exception) {
			return new Config();
		}
	}

	public void Save() {
		XmlSerializer serializer = new XmlSerializer(typeof(Config));
		using (StreamWriter file = File.CreateText(ConfigFileName)) {
			XmlWriterSettings settings = new XmlWriterSettings();
			settings.Indent = true;
			settings.IndentChars = "\t";
			using (XmlWriter writer = XmlWriter.Create(file, settings)) {
				serializer.Serialize(writer, this);
			}
		}
	}

}

public delegate void ImportAction();

public class ImportDialog : Form {

	private readonly Config config;
	private readonly ImportAction import;

	public ImportDialog(Config config, ImportAction import) {
		this.config = config;
		this.import = import;
		SuspendLayout();
		InitializeComponent();
		ResumeLayout(false);
	}

	private void InitializeComponent() {
		// Configure dialog
		Text = "Import Rhubarb";
		Size = new Size(600, 400);
		Font = new Font(Font.FontFamily, 10);

		// Add property grid
		PropertyGrid propertyGrid1 = new PropertyGrid();
		propertyGrid1.SelectedObject = config;
		Controls.Add(propertyGrid1);
		propertyGrid1.Dock = DockStyle.Fill;

		// Add button panel
		FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
		buttonPanel.FlowDirection = FlowDirection.RightToLeft;
		buttonPanel.AutoSize = true;
		buttonPanel.Dock = DockStyle.Bottom;
		Controls.Add(buttonPanel);

		// Add Cancel button
		Button cancelButton1 = new Button();
		cancelButton1.Text = "Cancel";
		cancelButton1.DialogResult = DialogResult.Cancel;
		buttonPanel.Controls.Add(cancelButton1);
		CancelButton = cancelButton1;

		// Add OK button
		Button okButton1 = new Button();
		okButton1.Text = "OK";
		okButton1.Click += OkButtonClickedHandler;
		buttonPanel.Controls.Add(okButton1);
		AcceptButton = okButton1;
	}

	private void OkButtonClickedHandler(object sender, EventArgs e) {
		try {
			import();
			DialogResult = DialogResult.OK;
		} catch (Exception exception) {
			MessageBox.Show(exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

}