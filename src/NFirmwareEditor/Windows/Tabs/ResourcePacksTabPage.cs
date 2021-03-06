﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using JetBrains.Annotations;
using NFirmware;
using NFirmwareEditor.Core;
using NFirmwareEditor.Managers;
using NFirmwareEditor.Models;

namespace NFirmwareEditor.Windows.Tabs
{
	internal partial class ResourcePacksTabPage : UserControl, IEditorTabPage
	{
		private readonly ResourcePackManager m_resourcePackManager;

		private IEnumerable<ResourcePackFile> m_allResourcePacks;
		private IEnumerable<ResourcePackFile> m_suitableResourcePacks;

		private Firmware m_firmware;

		public ResourcePacksTabPage([NotNull] ResourcePackManager resourcePackManager)
		{
			if (resourcePackManager == null) throw new ArgumentNullException("resourcePackManager");

			m_resourcePackManager = resourcePackManager;
			InitializeComponent();

			ResourcePackListView.Resize += (s, e) =>
			{
				NameColumnHeader.Width = ResourcePackListView.Width - VersionColumnHeader.Width - 1;
			};
			ResourcePackListView.SelectedIndexChanged += ResourcePackListView_SelectedIndexChanged;
			ResourcePackListView.ItemActivate += ResourcePackListView_ItemActivate;
			PreviewResourcePackButton.Click += PreviewResourcePackButton_Click;
			ImportResourcePackButton.Click += ImportResourcePackButton_Click;
			ReloadResourcePacksButton.Click += ReloadResourcePacksButton_Click;
		}

		[CanBeNull]
		public ResourcePackFile SelectedResourcePack
		{
			get
			{
				return ResourcePackListView.SelectedItems.Count == 0 ? null : ResourcePackListView.SelectedItems[0].Tag as ResourcePackFile;
			}
		}

		#region Implementation of IEditorTabPage
		public string Title
		{
			get { return "Resource Packs"; }
		}

		public void Initialize(IEditorTabPageHost host, Configuration configuration)
		{
			m_allResourcePacks = m_resourcePackManager.LoadAll();
		}

		public void OnWorkspaceReset()
		{
			ResourcePackListView.Items.Clear();
		}

		public void OnFirmwareLoaded(Firmware firmware)
		{
			m_firmware = firmware;

			m_suitableResourcePacks = m_allResourcePacks.Where(x => string.Equals(x.Definition, m_firmware.Definition.Name));
			ResourcePackListView.Fill(m_suitableResourcePacks.Select(resourcePack => new ListViewItem(new[]
			{
				resourcePack.Name,
				resourcePack.Version
			}) { Tag = resourcePack }));

			ImportResourcePackButton.Enabled = true;
			ReloadResourcePacksButton.Enabled = true;
		}

		public void OnActivate()
		{
		}

		public bool OnHotkey(Keys keyData)
		{
			return false;
		}
		#endregion

		private void PreviewResourcePack([NotNull] ResourcePack resourcePack)
		{
			if (resourcePack == null) throw new ArgumentNullException("resourcePack");
			if (resourcePack.Images == null || resourcePack.Images.Count == 0) return;

			var originalImageIndices = new List<int>();
			var importedImages = new List<bool[,]>();

			foreach (var exportedImage in resourcePack.Images)
			{
				originalImageIndices.Add(exportedImage.Index);
				importedImages.Add(exportedImage.Data);
			}

			using (var importWindow = new PreviewResourcePackWindow(m_firmware, originalImageIndices, importedImages, true))
			{
				importWindow.Text = Consts.ApplicationTitleWoVersion + @" - Resource Pack Preview";
				importWindow.ImportButtonText = "Import";
				if (importWindow.ShowDialog() != DialogResult.OK) return;

				ImportResourcePack(originalImageIndices, importedImages);
			}
		}

		private void ImportResourcePack([NotNull] IList<int> originalImageIndices, [NotNull] IList<bool[,]> importedImages)
		{
			if (importedImages == null) throw new ArgumentNullException("importedImages");
			if (originalImageIndices == null) throw new ArgumentNullException("originalImageIndices");
			if (importedImages.Count == 0) return;

			var block1MetadataDictionary = m_firmware.Block1Images.ToDictionary(x => x.Index, x => x);
			var block2MetadataDictionary = m_firmware.Block2Images.ToDictionary(x => x.Index, x => x);

			for (var i = 0; i < originalImageIndices.Count; i++)
			{
				var originalImageIndex = originalImageIndices[i];
				var importedImage = importedImages[i];

				if (block1MetadataDictionary.Count > 0)
				{
					var block1ImageMetadata = block1MetadataDictionary[originalImageIndex];
					var block1Image = FirmwareImageProcessor.PasteImage(block1ImageMetadata.CreateImage(), importedImage);
					m_firmware.WriteImage(block1Image, block1ImageMetadata);
				}

				if (block2MetadataDictionary.Count > 0)
				{
					var block2ImageMetadata = block2MetadataDictionary[originalImageIndex];
					var block2Image = FirmwareImageProcessor.PasteImage(block2ImageMetadata.CreateImage(), importedImage);
					m_firmware.WriteImage(block2Image, block2ImageMetadata);
				}
			}

			ImageCacheManager.RebuildImageCache(m_firmware);
		}

		private void ResourcePackListView_SelectedIndexChanged(object sender, EventArgs e)
		{
			PreviewResourcePackButton.Enabled = SelectedResourcePack != null;
			if (SelectedResourcePack == null) return;

			var sb = new StringBuilder();
			{
				sb.AppendLine("Author: " + SelectedResourcePack.Author);
				sb.AppendLine("Version: " + SelectedResourcePack.Version);
				sb.AppendLine();
				sb.AppendLine((SelectedResourcePack.Description ?? string.Empty).Trim().Replace("\n", Environment.NewLine));
			}
			DescriptionTextBox.Text = sb.ToString();
		}

		private void ResourcePackListView_ItemActivate(object sender, EventArgs e)
		{
			if (SelectedResourcePack == null) return;
			var resourcePack = m_resourcePackManager.LoadFromFile(SelectedResourcePack.FileName);
			if (resourcePack == null) return;

			PreviewResourcePack(resourcePack);
		}

		private void PreviewResourcePackButton_Click(object sender, EventArgs e)
		{
			if (SelectedResourcePack == null) return;
			var resourcePack = m_resourcePackManager.LoadFromFile(SelectedResourcePack.FileName);
			if (resourcePack == null) return;

			PreviewResourcePack(resourcePack);
		}

		private void ImportResourcePackButton_Click(object sender, EventArgs e)
		{
			string fileName;
			using (var op = new OpenFileDialog { Filter = Consts.ExportResourcePackFilter })
			{
				if (op.ShowDialog() != DialogResult.OK) return;
				fileName = op.FileName;
			}

			var resourcePack = m_resourcePackManager.LoadFromFile(fileName);
			if (resourcePack == null || string.IsNullOrEmpty(resourcePack.Definition)) return;
			if (resourcePack.Definition != m_firmware.Definition.Name)
			{
				InfoBox.Show("Selected resource pack is incompatible with the loaded firmware.\nResource pack is designed for: "
				             + resourcePack.Definition
				             + "\nOpend firmware is: "
				             + m_firmware.Definition.Name);
				return;
			}

			PreviewResourcePack(resourcePack);
		}

		private void ReloadResourcePacksButton_Click(object sender, EventArgs e)
		{
			m_allResourcePacks = m_resourcePackManager.LoadAll();

			OnWorkspaceReset();
			OnFirmwareLoaded(m_firmware);

			ResourcePackListView.Focus();
		}
	}
}
