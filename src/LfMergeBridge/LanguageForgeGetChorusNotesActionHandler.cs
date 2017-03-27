// Copyright (c) 2010-2016 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT) (See: license.rtf file)

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using Chorus;
using Chorus.merge;
using Chorus.notes;
using Chorus.Utilities;
using TriboroughBridge_ChorusPlugin;
using TriboroughBridge_ChorusPlugin.Infrastructure;
using LibTriboroughBridgeChorusPlugin;
using LibTriboroughBridgeChorusPlugin.Infrastructure;
using LibFLExBridgeChorusPlugin.Infrastructure;
using Palaso.Progress;

namespace FLEx_ChorusPlugin.Infrastructure.ActionHandlers
{
	/// <summary>
	/// This IBridgeActionTypeHandler implementation handles everything needed for viewing the notes of a Flex repo.
	/// </summary>
	[Export(typeof (IBridgeActionTypeHandler))]
	internal sealed class LanguageForgeGetChorusNotesActionHandler : IBridgeActionTypeHandler
	{
		public const string mainNotesFilenameStub = "Lexicon.fwstub";
		public const string chorusNotesExt = ".ChorusNotes";
		public const string mainNotesFilename = mainNotesFilenameStub + chorusNotesExt;
		public const string zeroGuidStr = "00000000-0000-0000-0000-000000000000";
		public const string genericAuthorName = "Language Forge";
		internal string ProjectName { get; set; }
		internal string ProjectDir { get; set; }

		#region IBridgeActionTypeHandler impl

		/// <summary>
		/// Start doing whatever is needed for the supported type of action.
		/// </summary>
		void IBridgeActionTypeHandler.StartWorking(IProgress progress, Dictionary<string, string> options, ref string somethingForClient)
		{
			var pOption = options["-p"];
			ProjectName = Path.GetFileNameWithoutExtension(pOption);
			ProjectDir = Path.GetDirectoryName(pOption);

			// TODO: Use a better parameter name, a static string stored in LfMergeBridge like other actions do
			// NOTE that it needs to be the same parameter name that's used in the "Write to Chorus Notes" handler, since the two of them can use the same input file
			// TODO: Also move that DecodeInputFile function out into its own class, since both handlers are going to use it
			//           - Actually, that "own class" could mostly do two-way work, and have both handlers call ONE of its two "work" functions. Something to consider, at least.
			// TODO: Decide whether we just want to pull ALL annotations, or just changed ones. If we want to pull just changed ones, uncomment the two lines below
			// List<SerializableLfAnnotation> dataFromLF = LanguageForgeWriteToChorusNotesActionHandler.DecodeInputFile(options["-i"]);
			// Dictionary<string, SerializableLfAnnotation> lfAnns = dataFromLF.ToDictionary(ann => ann.Guid, ann => ann);
			// ... and now do something with this

			var utf8 = new UTF8Encoding(false);
			var result = new StringBuilder();
			var json = new DataContractJsonSerializer(typeof(IEnumerable<SerializableLfAnnotation>));
			var lfAnns = new List<SerializableLfAnnotation>();
			foreach (Annotation ann in GetAllAnnotations(ProjectDir))
			{
				var msg = ann.Messages.FirstOrDefault();
				var lfComment = new SerializableLfAnnotation {
					Guid = ann.Guid,
					// Author = msg?.Author ?? string.Empty,
					AuthorNameAlternate = (msg == null) ? string.Empty : msg.Author,
					DateCreated = ann.Date,  // TODO: Local or UTC?
					DateModified = ann.Date, // Same consideration
					// Content = msg?.Text ?? string.Empty,
					Content = (msg == null) ? string.Empty : msg.Text,
					Status = ChorusStatusToLfStatus(ann.Status),
					Replies = new List<SerializableLfCommentReply>(),
					IsDeleted = false
				};
				lfComment.Regarding = new SerializableLfCommentRegarding {
					TargetGuid = ExtractGuidFromChorusRef(ann.RefStillEscaped),
					Word = ann.LabelOfThingAnnotated, // TODO: Might have to set this one in LfMerge, using the Guid to find the right word and meaning
					Meaning = string.Empty  // TODO: Have to set this one in LfMerge; see above
				};
				lfAnns.Add(lfComment);
			}
			// Sigh... but .Net only offers WriteObject(stream, object), not MakeString(object)
			using (var stream = new MemoryStream())
			{
				json.WriteObject(stream, lfAnns);
				stream.Flush();
				result.Append(Encoding.UTF8.GetString(stream.GetBuffer()));
			}
			// Send results back to LfMerge
			// var tmpFile = new Palaso.IO.TempFile(result.ToString());  // Deliberately NOT using "using" because we don't want to dispose of this one.
			// LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, "JSON has been written to " + tmpFile.Path);
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, result.ToString());
		}

		private string ExtractGuidFromChorusRef(string refStillEscaped)
		{
			var repos = AnnotationRepository.CreateRepositoriesFromFolder(ProjectDir, new NullProgress());
			var repo = repos.First();
			// TODO: Delete the lines above
			return UrlHelper.GetValueFromQueryStringOfRef(refStillEscaped, "guid", string.Empty);
		}

		private string ChorusStatusToLfStatus(string status)
		{
			if (status == SerializableChorusAnnotation.Closed)
			{
				return SerializableLfAnnotation.Resolved;
			}
			else
			{
				return SerializableLfAnnotation.Open; // LfMerge will look at this and see if the Mongo DB contained "Todo".
			}
		}

        IEnumerable<Annotation> GetAllAnnotations(string projectDir)
		{
			var nullProgress = new NullProgress();  // TODO: Just pass in the progress object we were given!
			return from repo in AnnotationRepository.CreateRepositoriesFromFolder(projectDir, nullProgress)
				from ann in repo.GetAllAnnotations()
				select ann;
		}

		/// <summary>
		/// Get the type of action supported by the handler.
		/// </summary>
		ActionType IBridgeActionTypeHandler.SupportedActionType
		{
			get { return ActionType.LanguageForgeGetChorusNotes; }
		}

		#endregion IBridgeActionTypeHandler impl

		#region IDisposable impl

		/// <summary>
		/// Finalizer, in case client doesn't dispose it.
		/// Force Dispose(false) if not already called (i.e. m_isDisposed is true)
		/// </summary>
		~LanguageForgeGetChorusNotesActionHandler()
		{
			Dispose(false);
			// The base class finalizer is called automatically.
		}

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing,
		/// or resetting unmanaged resources.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			// This object will be cleaned up by the Dispose method.
			// Therefore, you should call GC.SupressFinalize to
			// take this object off the finalization queue
			// and prevent finalization code for this object
			// from executing a second time.
			GC.SuppressFinalize(this);
		}

		private bool IsDisposed { get; set; }

		/// <summary>
		/// Executes in two distinct scenarios.
		///
		/// 1. If disposing is true, the method has been called directly
		/// or indirectly by a user's code via the Dispose method.
		/// Both managed and unmanaged resources can be disposed.
		///
		/// 2. If disposing is false, the method has been called by the
		/// runtime from inside the finalizer and you should not reference (access)
		/// other managed objects, as they already have been garbage collected.
		/// Only unmanaged resources can be disposed.
		/// </summary>
		/// <remarks>
		/// If any exceptions are thrown, that is fine.
		/// If the method is being done in a finalizer, it will be ignored.
		/// If it is thrown by client code calling Dispose,
		/// it needs to be handled by fixing the issue.
		/// </remarks>
		private void Dispose(bool disposing)
		{
			if (IsDisposed)
				return;

			ProjectName = null;
			ProjectDir = null;

			IsDisposed = true;

			// You know, I don't think we *need* to be IDisposable any more...
		}

		#endregion IDisposable impl
	}
}