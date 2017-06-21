// Copyright (c) 2010-2016 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT) (See: license.rtf file)

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Newtonsoft.Json;
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

			// TODO: Consider whether to move that DecodeInputFile function out into its own class, since both handlers are going to use it
			//           - Actually, that "own class" could mostly do two-way work, and have both handlers call ONE of its two "work" functions. Something to consider, at least.

			string inputFilename = options[LfMergeBridge.LfMergeBridgeUtilities.serializedCommentsFromLfMerge];
			List<SerializableLfComment> commentsFromLF = LanguageForgeWriteToChorusNotesActionHandler.DecodeInputFile<List<SerializableLfComment>>(inputFilename);
			var knownCommentGuids = new HashSet<string>(commentsFromLF.Where(comment => comment.Guid != null).Select(comment => comment.Guid));
			var knownReplyGuids = new HashSet<string>(commentsFromLF.Where(comment => comment.Replies != null).SelectMany(comment => comment.Replies.Where(reply => reply.Guid != null).Select(reply => reply.Guid)));

			var lfComments = new List<SerializableLfComment>();
			var lfReplies = new List<Tuple<string, List<SerializableLfCommentReply>>>();
			foreach (Annotation ann in GetAllAnnotations(ProjectDir))
			{
				if (knownCommentGuids.Contains(ann.Guid))
				{
					// Known comment; only serialize new replies
					List<SerializableLfCommentReply> repliesNotYetInLf =
						ann
							.Messages
							.Skip(1)  // First message translates to the LF *comment*, while subsequent messages are *replies* in LF
							.Where(m => ! String.IsNullOrWhiteSpace(m.Text))
							.Where(m => ! knownReplyGuids.Contains(m.Guid))
							.Select(ReplyFromChorusMsg)
							.ToList();
					if (repliesNotYetInLf.Count > 0)
					{
						lfReplies.Add(new Tuple<string, List<SerializableLfCommentReply>>(ann.Guid, repliesNotYetInLf));
					}
				}
				else
				{
					// New comment: serialize everything
					var msg = ann.Messages.FirstOrDefault();
					var lfComment = new SerializableLfComment {
						Guid = ann.Guid,
						// Author = msg?.Author ?? string.Empty,
						AuthorNameAlternate = (msg == null) ? string.Empty : msg.Author,
						DateCreated = ann.Date,  // TODO: Local or UTC?
						DateModified = ann.Date, // Same consideration
						// Content = msg?.Text ?? string.Empty,
						Content = (msg == null) ? string.Empty : msg.Text,
						Status = ChorusStatusToLfStatus(ann.Status),
						Replies = new List<SerializableLfCommentReply>(ann.Messages.Skip(1).Where(m => ! String.IsNullOrWhiteSpace(m.Text)).Select(ReplyFromChorusMsg)),
						IsDeleted = false
					};
					lfComment.Regarding = new SerializableLfCommentRegarding {
						TargetGuid = ExtractGuidFromChorusRef(ann.RefStillEscaped),
						Word = ann.LabelOfThingAnnotated, // TODO: Might have to set this one in LfMerge, using the Guid to find the right word and meaning
						Meaning = string.Empty  // TODO: Have to set this one in LfMerge; see above
					};
					lfComments.Add(lfComment);
				}
			}
			var serializedComments = new StringBuilder("New comments not yet in LF: ");
			serializedComments.Append(JsonConvert.SerializeObject(lfComments));
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, serializedComments.ToString());

			var serializedReplies = new StringBuilder("New replies on comments already in LF: ");
			serializedReplies.Append(JsonConvert.SerializeObject(lfReplies));
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, serializedReplies.ToString());
		}

		private SerializableLfCommentReply ReplyFromChorusMsg(Message msg)
		{
			var reply = new SerializableLfCommentReply();
			reply.Guid = msg.Guid;
			reply.AuthorNameAlternate = msg.Author;
			if (reply.AuthorInfo == null)
				reply.AuthorInfo = new SerializableLfAuthorInfo();
			reply.AuthorInfo.CreatedDate = msg.Date;
			reply.AuthorInfo.ModifiedDate = msg.Date;
			reply.Content = msg.Text;
			reply.IsDeleted = false;
			reply.UniqId = null; // This will be set in LfMerge.
			return reply;
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
				return SerializableLfComment.Resolved;
			}
			else
			{
				return SerializableLfComment.Open; // LfMerge will look at this and see if the Mongo DB contained "Todo".
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
