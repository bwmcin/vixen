﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Vixen.Module.Analysis;
using VixenModules.Media.Audio;
using VixenModules.Sequence.Timed;
using QMLibrary;

namespace VixenModules.Analysis.BeatsAndBars
{
	public class BeatsAndBars : AnalysisModuleInstanceBase
	{
		private ManagedPlugin m_plugin = null;
		private IDictionary<int, ICollection<ManagedFeature>> m_featureSet;
		private Audio m_audioModule = null;

		private byte[] m_bSamples;
		private float[] m_fSamplesAll;
		private float[] m_fSamplesPreview;

		private const int PREVIEW_TIME = 10;

		public BeatsAndBars(Audio module)
		{
			m_audioModule = module;
			m_featureSet = null;
		}

		public override void Loading() { }
		public override void Unloading() { }

		private IDictionary<int, ICollection<ManagedFeature>> GenerateFeatures(ManagedPlugin plugin, float[] fSampleData, bool showProgress = true)
		{
			int i = 0;
			int j = 0;

			BeatsAndBarsProgress progressDlg = new BeatsAndBarsProgress();
			if (showProgress)
			{
				progressDlg.Show();	
			}
			
			int stepSize = plugin.GetPreferredStepSize();

			double progressVal = 0;
			float[] fSamples = new float[plugin.GetPreferredBlockSize()];
			for (j = 0;
				((fSampleData.Length - j) >= plugin.GetPreferredBlockSize());
				j += stepSize)
			{
				progressVal = ((double)j / (double) fSampleData.Length) * 100.0;
				progressDlg.UpdateProgress((int) progressVal);

				Array.Copy(fSampleData, j, fSamples, 0, fSamples.Length);
				plugin.Process(fSamples,
						ManagedRealtime.frame2RealTime(j, (uint)m_audioModule.Frequency));
			}

			Array.Clear(fSamples, 0, fSamples.Length);
			Array.Copy(fSampleData, j, fSamples, 0, fSampleData.Length - j);
			plugin.Process(fSamples,
					ManagedRealtime.frame2RealTime(j, (uint)m_audioModule.Frequency));

			progressDlg.Close();

			return plugin.GetRemainingFeatures();
			
		}

		private MarkCollection 
			ExtractAllMarksFromFeatureSet(ICollection<ManagedFeature> featureSet, 
											BeatBarSettingsData settings)
		{
			MarkCollection mc = new MarkCollection();
			mc.Enabled = true;
			mc.Name = settings.AllCollectionName;

			double lastFeatureMS = -1;
			double featureMS = -1;

			foreach (ManagedFeature feature in featureSet)
			{
				if (feature.hasTimestamp)
				{
					featureMS = feature.timestamp.totalMilliseconds();
					if (lastFeatureMS != -1)
					{
						double interval = (featureMS - lastFeatureMS) / settings.Divisions;
						for (int j = 0; j < settings.Divisions; j++)
						{
							mc.Marks.Add(TimeSpan.FromMilliseconds(lastFeatureMS + (interval * j)));
						}
					}
					else
					{
						mc.Marks.Add(TimeSpan.FromMilliseconds(featureMS));
					}
					lastFeatureMS = featureMS;
				}
			}
			return mc;
		}

		private MarkCollection 
			ExtractBarMarksFromFeatureSet(ICollection<ManagedFeature> featureSet, 
											BeatBarSettingsData settings)
		{
			MarkCollection mc = new MarkCollection();
			mc.Enabled = true;
			mc.Name = settings.BarsCollectionName;

			double featureMS = -1;

			foreach (ManagedFeature feature in featureSet)
			{
				if (feature.hasTimestamp)
				{
					featureMS = feature.timestamp.totalMilliseconds();
					mc.Marks.Add(TimeSpan.FromMilliseconds(featureMS));
				}
			}
			return mc;
		}

		private List<MarkCollection> 
			ExtractBeatCollectionsFromFeatureSet(ICollection<ManagedFeature> featureSet, 
													BeatBarSettingsData settings)
		{
			List<MarkCollection> retVal = new List<MarkCollection>();
			string[] collectionNames = settings.BeatCollectionNames(false);

			for (int j = 1; j <= collectionNames.Length; j++)
			{
				MarkCollection mc = new MarkCollection();
				mc.Enabled = true;
				mc.Name = collectionNames[j-1];

				double featureMS = -1;

				foreach (ManagedFeature feature in featureSet)
				{
					if ((feature.hasTimestamp) && (feature.label == j.ToString()))
					{
						featureMS = feature.timestamp.totalMilliseconds();
						mc.Marks.Add(TimeSpan.FromMilliseconds(featureMS));
					}
				}
				retVal.Add(mc);
			}
			return retVal;
		}

		private List<MarkCollection> 
			ExtractSplitCollectionsFromFeatureSet(ICollection<ManagedFeature> featureSet, 
													BeatBarSettingsData settings)
		{		
			List<MarkCollection> retVal = new List<MarkCollection>();
			string[] collectionNames = settings.BeatCollectionNames(true);
			KeyValuePair<int,double>[] tsValuePairs = new KeyValuePair<int, double>[(featureSet.Count * 2)];

			int count = 0;

			double featureMS = -1;
			double lastFeatureMS = -1;
			int labelVal = 0;

			ManagedFeature lastFeature = null;

			foreach (ManagedFeature feature in featureSet)
			{
				if (lastFeature == null)
				{
					lastFeature = feature;
					continue;
				}

				labelVal = (Convert.ToInt32(lastFeature.label) * 2) - 1;
				lastFeatureMS = lastFeature.timestamp.totalMilliseconds();

				tsValuePairs[count++] =
					new KeyValuePair<int, double>(labelVal, lastFeatureMS);

				featureMS = feature.timestamp.totalMilliseconds();
				tsValuePairs[count] =
					new KeyValuePair<int, double>(labelVal + 1,
						lastFeatureMS + ((featureMS - lastFeatureMS) / settings.Divisions));

				count++;
				lastFeature = feature;
			}

			for (int j = 1; j <= collectionNames.Length; j++)
			{
				MarkCollection mc = new MarkCollection();
				mc.Enabled = true;
				mc.Name = collectionNames[j - 1];

				foreach (KeyValuePair<int,double> tsValue in tsValuePairs)
				{
					if (tsValue.Key == j)
					{
						mc.Marks.Add(TimeSpan.FromMilliseconds(tsValue.Value));
					}
				}

				retVal.Add(mc);
			}
			return retVal;
		}

		private double EstimateBeatPeriod(ICollection<ManagedFeature> features)
		{
			double retVal = 0;
			double featureMS = -1;
			double lastFeatureMS = -1;
			bool startCalcs = false;

			foreach (ManagedFeature feature in features)
			{
				startCalcs = (feature.label.Equals("1") ? true : startCalcs);

				if ((feature.hasTimestamp) && (startCalcs))
				{
					featureMS = feature.timestamp.totalMilliseconds();

					if ((lastFeatureMS != -1) && (retVal == 0))
					{
						retVal = featureMS - lastFeatureMS;
					}

					if (lastFeatureMS > 0)
					{
						retVal = (retVal + (featureMS - lastFeatureMS)) / 2;
					}

					lastFeatureMS = featureMS;
				}
			}
			return retVal;
		}

		private BeatBarPreviewData GeneratePreviewData()
		{
			BeatBarPreviewData previewData = new BeatBarPreviewData(1);
			QMBarBeatTrack plugin = new QMBarBeatTrack(m_audioModule.Frequency);
			plugin.SetParameter("bpb", 4);

			plugin.Initialise(1,
				(uint)plugin.GetPreferredStepSize(),
				(uint)plugin.GetPreferredBlockSize());


			IDictionary<int, ICollection<ManagedFeature>> featureSet = GenerateFeatures(plugin, m_fSamplesPreview, false);
			previewData.BeatPeriod = EstimateBeatPeriod(featureSet[2]);

			BeatBarSettingsData settings = new BeatBarSettingsData("Preview");
			settings.Divisions = 1;
			settings.BeatSplitsEnabled = false;
			settings.NoteSize = 4;
			settings.BeatsPerBar = 4;

			List<MarkCollection> collections = ExtractBeatCollectionsFromFeatureSet(featureSet[2], settings);
			MarkCollection allCollection = new MarkCollection();
			allCollection.Name = "Beat Marks";
			collections.ForEach(x => allCollection.Marks.AddRange(x.Marks));
			allCollection.Marks.Sort();
			previewData.PreviewCollection = allCollection;

			settings.BeatSplitsEnabled = true;
			settings.Divisions = 2;
			collections = ExtractSplitCollectionsFromFeatureSet(featureSet[2], settings);
			allCollection = new MarkCollection();
			allCollection.Name = "Beat Split Marks";
			collections.ForEach(x => allCollection.Marks.AddRange(x.Marks));
			allCollection.Marks.Sort();
			previewData.PreviewSplitCollection = allCollection;

			return previewData;
		}

		private List<MarkCollection> BuildMarkCollections(List<MarkCollection> markCollection, 
															BeatBarSettingsData settings)
		{
			List<MarkCollection> retVal = markCollection;

			m_featureSet = GenerateFeatures(m_plugin, m_fSamplesAll);
			String[] beatCollectionNames = settings.BeatCollectionNames(false);
			String[] splitCollectionNames = settings.BeatCollectionNames(true);

			if (settings.AllFeaturesEnabled)
			{
				markCollection.RemoveAll(x => x.Name.Equals(settings.AllCollectionName));
				MarkCollection mc = ExtractAllMarksFromFeatureSet(m_featureSet[0], settings);
				mc.MarkColor = settings.AllFeaturesColor;
				retVal.Add(mc);
			}

			if (settings.BarsEnabled)
			{
				markCollection.RemoveAll(x => x.Name.Equals(settings.BarsCollectionName));
				MarkCollection mc = ExtractBarMarksFromFeatureSet(m_featureSet[1], settings);
				mc.MarkColor = settings.BarsColor;
				retVal.Add(mc);
			}

			if (settings.BeatCollectionsEnabled)
			{
				foreach (String name in beatCollectionNames)
				{
					markCollection.RemoveAll(x => x.Name.Equals(name));
				}
				List<MarkCollection> mcl = ExtractBeatCollectionsFromFeatureSet(m_featureSet[2], settings);
				mcl.ForEach(x => x.MarkColor = settings.BeatCountsColor);
				retVal.AddRange(mcl);
			}

			if (settings.BeatSplitsEnabled)
			{
				foreach (String name in splitCollectionNames)
				{
					markCollection.RemoveAll(x => x.Name.Equals(name));
				}
				List<MarkCollection> mcl = ExtractSplitCollectionsFromFeatureSet(m_featureSet[2], settings);
				mcl.ForEach(x => x.MarkColor = settings.BeatSplitsColor);
				retVal.AddRange(mcl);
			}

			return retVal;
		}

		public List<MarkCollection> DoBeatBarDetection(List<MarkCollection> markCollection)
		{
			List<MarkCollection> retVal = markCollection;
			m_plugin = new QMBarBeatTrack(m_audioModule.Frequency);

			m_audioModule.LoadMedia(TimeSpan.Zero);
			m_bSamples = m_audioModule.GetSamples(0, (int)m_audioModule.NumberSamples);
			m_fSamplesAll = new float[m_audioModule.NumberSamples];
			m_fSamplesPreview = new float[(int)(m_audioModule.Frequency * PREVIEW_TIME)];

			int dataStep = m_audioModule.BytesPerSample;

			for (int j = 0, sampleNum = 0; j < m_bSamples.Length; j += dataStep, sampleNum++)
			{
				m_fSamplesAll[sampleNum] = dataStep == 2 ? 
					BitConverter.ToInt16(m_bSamples, j) : BitConverter.ToInt32(m_bSamples, j);
			}

			Array.Copy(m_fSamplesAll, m_fSamplesPreview, m_fSamplesPreview.Length);

			BeatsAndBarsDialog bbSettings = new BeatsAndBarsDialog(m_audioModule);
			bbSettings.PreviewData = GeneratePreviewData();
			bbSettings.MarkCollectionList = markCollection;

			DialogResult result = bbSettings.ShowDialog();
			if (result == DialogResult.OK)
			{
				m_plugin.SetParameter("bpb", bbSettings.Settings.BeatsPerBar);

				m_plugin.Initialise(1,
					(uint)m_plugin.GetPreferredStepSize(),
					(uint)m_plugin.GetPreferredBlockSize());

				retVal = BuildMarkCollections(markCollection, bbSettings.Settings );
			}

			return retVal;
		}

	}

	public class BeatBarPreviewData
	{
		public BeatBarPreviewData(double period)
		{
			BeatPeriod = period;
		}

		public double BeatPeriod { get; set; }
		public MarkCollection PreviewCollection { get; set; }
		public MarkCollection PreviewSplitCollection { get; set; }
	}

	public class BeatBarSettingsData
	{
		public bool BarsEnabled { get; set; }
		public bool BeatCollectionsEnabled { get; set; }
		public bool BeatSplitsEnabled { get; set; }
		public bool AllFeaturesEnabled { get; set; }
		public String CollectionBaseName { get; set; }

		public int Divisions { get; set; }

		public Color AllFeaturesColor { get; set; }
		public Color BarsColor { get; set; }
		public Color BeatCountsColor { get; set; }
		public Color BeatSplitsColor { get; set; }

		public int BeatsPerBar { get; set; }
		public int NoteSize { get; set; }

		public BeatBarPreviewData PreviewData { get; set; }

		public String AllCollectionName
		{
			get { return CollectionBaseName + " - All"; }
		}

		public String BarsCollectionName
		{
			get { return CollectionBaseName + " - Bars"; }
		}

		public String[] BeatCollectionNames(bool addDivisions)
		{
			int collections = BeatsPerBar * ((addDivisions) ? Divisions : 1);
			int actualNoteSize = NoteSize * ((addDivisions) ? Divisions : 1);

			String[] retVal = new string[collections];

			for (int j = 0; j < collections; j++)
			{
				retVal[j] = CollectionBaseName + " 1/" + actualNoteSize + " Note - " + (j + 1);
			}

			return retVal;
		}

		public BeatBarSettingsData(String collectionBaseName)
		{
			BarsEnabled = false;
			BeatCollectionsEnabled = false;
			BeatSplitsEnabled = false;
			AllFeaturesEnabled = false;

			CollectionBaseName = collectionBaseName;
			AllFeaturesColor = Color.White;
			BarsColor = Color.White;
			BeatCountsColor = Color.White;
			BeatSplitsColor = Color.White;
			Divisions = 1;
		}

	}
}
