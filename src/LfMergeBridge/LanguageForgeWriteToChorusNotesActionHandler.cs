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
using TriboroughBridge_ChorusPlugin;
using TriboroughBridge_ChorusPlugin.Infrastructure;
using LibTriboroughBridgeChorusPlugin;
using LibTriboroughBridgeChorusPlugin.Infrastructure;
using LibFLExBridgeChorusPlugin.Infrastructure;
using Palaso.Progress;

namespace FLEx_ChorusPlugin.Infrastructure.ActionHandlers
{
	/// <summary>
	/// This IBridgeActionTypeHandler implementation handles writing new notes (which probably came from comments on the Language Forge site) into the ChorusNotes system.
	/// </summary>
	[Export(typeof (IBridgeActionTypeHandler))]
	internal sealed class LanguageForgeWriteToChorusNotesActionHandler : IBridgeActionTypeHandler
	{
		public const string mainNotesFilenameStub = "Lexicon.fwstub";
		public const string chorusNotesExt = ".ChorusNotes";
		public const string mainNotesFilename = mainNotesFilenameStub + chorusNotesExt;
		public const string zeroGuidStr = "00000000-0000-0000-0000-000000000000";
		public const string genericAuthorName = "Language Forge";

		internal string ProjectName { get; set; }
		internal string ProjectDir { get; set; }

		private IProgress Progress { get; set; }

		#region IBridgeActionTypeHandler impl

		/// <summary>
		/// Start doing whatever is needed for the supported type of action.
		/// </summary>
		void IBridgeActionTypeHandler.StartWorking(IProgress progress, Dictionary<string, string> options, ref string somethingForClient)
		{
			// We need to be handed the following things for each note:
			//   - Its GUID in Chorus
			//   - Its content (a string)
			//   - Its owner's basic required information, as follows:
			//       - Owner's GUID
			//       - List of GUIDs of any objects the owner owns (use .AllOwnedObjects.Select(x => x.Guid.ToString().ToLowerInvariant())
			//       - Owner's ShortName property  (might have to compute that for created-in-LF objects. In FW the LexEntry.HeadWordStatic function is used, which uses StringServices.HeadWordForWsAndHn with the default vernacular Ws and entry.HomographNumber, defaulting to ??? if nothing can be found, and HeadwordVariant.Main, which then uses the CitationFormWithAffixTypeStaticFowWs function. If that function doesn't figure it out, then it uses AddHeadwordForWsAndHn. The former gets the citation form and, if it doesn't exist, the lexeme form. If neither of those have a default-vernacular string, then it looks in AlternateFormsOS for the first one with a default-vern string. And if all of THAT still fails, then it returns the ??? strings. And so the AddHeadwordForWsAndHn function pretty much never gets called...)
			//           - Note that the ShortName goes into the Label, so it might not be necessary to precisely match FW here. OTOH, it might be necessary to avoid later confusion, so...
			//           - So the summary of that logic is: try the citation form. If that isn't it, try the lexeme form. If that isn't it, give up and return "???" (because LF doesn't do Alternate Forms).
			var pOption = options["-p"];
			ProjectName = Path.GetFileNameWithoutExtension(pOption);
			ProjectDir = Path.GetDirectoryName(pOption);
			Progress = progress;

			// TODO: Use a better parameter name, a static string stored in LfMergeBridge like other actions do
			List<SerializableLfAnnotation> dataFromLF = DecodeInputFile(options["-i"]);
			AnnotationRepository[] annRepos = GetAnnotationRepositories();
			AnnotationRepository primaryRepo = annRepos[0];

			// This does NOT work, because there can be duplicate keys (why?)
			// Dictionary<string, Annotation> chorusAnnotationsByGuid = annRepos.SelectMany(repo => repo.GetAllAnnotations()).ToDictionary(ann => ann.Guid, ann => ann);
			// Instead we have to do it by hand:
			var chorusAnnotationsByGuid = new Dictionary<string, Annotation>();
			foreach (Annotation ann in annRepos.SelectMany(repo => repo.GetAllAnnotations()))
			{
				// if (chorusAnnotationsByGuid.ContainsKey(ann.Guid))
				// {
				// 	var oldAnn = chorusAnnotationsByGuid[ann.Guid];
				// 	LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("Duplicate annotation GUID detected: {0} was already an annotation containing messages [{1}], and we just tried to add an annotation containing messages [{2}]",
				// 		oldAnn.Guid,
				// 		String.Join(", ", oldAnn.Messages.Select(msg => "\"" + msg.Text + "\"")),
				// 		String.Join(", ",    ann.Messages.Select(msg => "\"" + msg.Text + "\""))));
				// }
				chorusAnnotationsByGuid[ann.Guid] = ann;
			}

			// Next step: go through all the annotations in the project dir and keep track of their GUIDs.
			// Then decide whether we need to add anything to them because the Messages have changed.
			// First criterion: how many messages are there?
			// Second criterion: go through either *content*, or *date modified*. (Decide which one). Probably date modified. (or AuthorInfo.DateModified)

			var uniqIdsThatNeedGuids = new Dictionary<string,string>();

			foreach (var lfAnnotation in dataFromLF)
			{
				if (lfAnnotation.IsDeleted)
				{
					LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("Skipping deleted annotation {0} containing content \"{1}\"",
						lfAnnotation.Guid, lfAnnotation.Content));
					continue;
				}
				// string ownerGuid = lfAnnotation.Regarding?.TargetGuid ?? string.Empty;
				// string ownerShortName = lfAnnotation.Regarding?.Word ?? "???";  // Match FLEx's behavior when short name can't be determined
				string ownerGuid = (lfAnnotation.Regarding == null) ? string.Empty : lfAnnotation.Regarding.TargetGuid;
				string ownerShortName = (lfAnnotation.Regarding == null) ? "???" : lfAnnotation.Regarding.Word;  // Match FLEx's behavior when short name can't be determined

				Annotation chorusAnnotation;
				if (lfAnnotation.Guid != null && chorusAnnotationsByGuid.TryGetValue(lfAnnotation.Guid, out chorusAnnotation) && chorusAnnotation != null)
				{
					SetChorusAnnotationMessagesFromLfReplies(chorusAnnotation, lfAnnotation, uniqIdsThatNeedGuids);
					LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("Wrote annotation {0} containing messages [{1}]",
						chorusAnnotation.Guid, String.Join(", ", chorusAnnotation.Messages.Select(msg => "\"" + msg.Text + "\""))));
					if (lfAnnotation.Replies == null || lfAnnotation.Replies.Count == 0)
						LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, "... and it had no replies.");
				}
				else
				{
					Annotation newAnnotation = CreateAnnotation(lfAnnotation.Content, lfAnnotation.Guid, lfAnnotation.AuthorNameAlternate, lfAnnotation.Status, ownerGuid, ownerShortName);
					SetChorusAnnotationMessagesFromLfReplies(newAnnotation, lfAnnotation, uniqIdsThatNeedGuids);
					LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("*NEW* annotation {0} containing messages [{1}]",
						newAnnotation.Guid, String.Join(", ", newAnnotation.Messages.Select(msg => "\"" + msg.Text + "\""))));
					if (lfAnnotation.Replies == null || lfAnnotation.Replies.Count == 0)
						LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, "... which had no replies.");
					// XYZZY commenting out
					// primaryRepo.AddAnnotation(newAnnotation);
				}
			}

			// TODO: At this point, some of the lfAnnotation objects may have new GUIDs in their Replies list (because
			// those replies were new, or were created before LF grew a GUID field on replies.) We need to find those
			// new GUIDs and send them back over the wall to LfMerge so that they can go into Mongo. Otherwise, those
			// new or GUIDless replies will look new the *next* time we S/R, and we'll end up duplicating them.

			// TODO: We now have that data in "uniqIdsThatNeedGuids". Send it back over the wall.
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("Total of {0} reply ID->Guid mappings found", uniqIdsThatNeedGuids.Count));
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("New reply ID->Guid mappings: {0}",
				String.Join(";", uniqIdsThatNeedGuids.Select(kv => String.Format("{0}={1}", kv.Key, kv.Value)))));


			// XYZZY commenting out
			// SaveReposIfNeeded(annRepos, progress);
		}

		private void SaveReposIfNeeded(IEnumerable<AnnotationRepository> repos, IProgress progress)
		{
			foreach (var repo in repos)
			{
				repo.SaveNowIfNeeded(progress);
			}
		}

		private bool MessageIsLikeReply(Message chorusMsg, SerializableLfCommentReply lfReply)
		{
			// return (chorusMsg.GetHtmlText(null) == lfReply.Text);  // TODO: What do we do about the handler repositories?
			// Answer: Need to implement IEmbeddedMessageContentHandler and put that in the repo. It will extract appropriate text from the conflict msg.

			// Above is how we had been doing it, but now we're just going to compare GUIDs instead. This means that if the LF comment gets edited
			// after a S/R, the edit will *not* appear in FLEx, which will continue to have the original version of the comment.
			return (chorusMsg.Guid == lfReply.Guid);
		}

		private string LfStatusToChorusStatus(string lfStatus)
		{
			if (lfStatus == SerializableLfAnnotation.Resolved)
			{
				return SerializableChorusAnnotation.Closed;
			}
			else
			{
				return SerializableChorusAnnotation.Open;
			}
		}

		private bool AnnotationsAreAlike(Annotation chorusAnnotation, SerializableLfAnnotation annotationInfo)
		{
			// Since we currently don't have message Guids, we have to compare contents
			if (chorusAnnotation.Messages.Count() != annotationInfo.Replies.Count)
			{
				return false;
			}
			foreach (var pair in chorusAnnotation.Messages.Zip(annotationInfo.Replies, (msg, reply) => new { Msg = msg, Reply = reply }))
			{
				if ( ! MessageIsLikeReply(pair.Msg, pair.Reply))
				{
					return false;
				}
			}
			return true;
		}

		private void SetChorusAnnotationMessagesFromLfReplies(Annotation chorusAnnotation, SerializableLfAnnotation annotationInfo, Dictionary<string,string> uniqIdsThatNeedGuids)
		{
			// TODO: We'll need another parameter, or else a private instance variable, to build up a list of (PHP id, GUID) pairs
			// for communicating back to LfMerge at the end. I'd prefer another parameter, as an instance variable just hides the
			// dependency and makes it harder to test.

			if (annotationInfo.Replies == null || annotationInfo.Replies.Count <= 0)
			{
				return;  // Nothing to do!
			}

			var chorusMsgGuids = new HashSet<string>(chorusAnnotation.Messages.Select(msg => msg.Guid).Where(s => ! string.IsNullOrEmpty(s) && s != Guid.Empty.ToString() ));
			string statusToSet = LfStatusToChorusStatus(annotationInfo.Status);
			// If we're in this function, the Chorus annotation already contains the text of the LF annotation's comment,
			// so the only thing we need to go through are the replies.
			foreach (SerializableLfCommentReply reply in annotationInfo.Replies)
			{
				if (reply.IsDeleted || chorusMsgGuids.Contains(reply.Guid))
				{
					continue;
				}
				// XYZZY commenting out
				Message newChorusMsg = chorusAnnotation.AddMessage(reply.AuthorNameAlternate, statusToSet, reply.Text);
				if ((string.IsNullOrEmpty(reply.Guid) || reply.Guid == zeroGuidStr) && ! string.IsNullOrEmpty(reply.UniqId))
				{
					uniqIdsThatNeedGuids[reply.UniqId] = newChorusMsg.Guid;
					// uniqIdsThatNeedGuids[reply.UniqId] = Guid.NewGuid().ToString(); // Just for testing purposes. TODO: Remove this line entirely.
				}
			}
			// Since LF allows changing a comment's status without adding any replies, it's possible we haven't updated the Chorus status yet at this point
			if (statusToSet != chorusAnnotation.Status)
			{
				// LF doesn't keep track of who clicked on the "Resolved" or "Todo" buttons, so we have to be vague about authorship
				chorusAnnotation.SetStatus(genericAuthorName, statusToSet);
			}
		}

		private AnnotationRepository[] GetAnnotationRepositories()
		{
			AnnotationRepository[] projectRepos = AnnotationRepository.CreateRepositoriesFromFolder(ProjectDir, new NullProgress()).ToArray();
			// Order of these repos doesn't matter, *except* that we want the "main" repo to be first in the array
			if (projectRepos.Length <= 0)
			{
				var primaryRepo = MakePrimaryAnnotationRepository();
				return new AnnotationRepository[] { primaryRepo };
			}
			else
			{
				int idx = Array.FindIndex(projectRepos, repo => repo.AnnotationFilePath.Contains(mainNotesFilename));
				if (idx < 0)
				{
					var primaryRepo = MakePrimaryAnnotationRepository();
					var result = new AnnotationRepository[projectRepos.Length + 1];
					result[0] = primaryRepo;
					Array.Copy(projectRepos, 0, result, 1, projectRepos.Length);
					return result;
				}
				else if (idx == 0)
				{
					return projectRepos;
				}
				else
				{
					// Since order of the other repos doesn't matter, just swap the primary into first position
					var primaryRepo = projectRepos[idx];
					projectRepos[idx] = projectRepos[0];
					projectRepos[0] = primaryRepo;
					return projectRepos;
				}
			}
		}

		private AnnotationRepository MakePrimaryAnnotationRepository()
		{
			string fname = Path.Combine(ProjectDir, mainNotesFilenameStub);
			EnsureFileExists(fname, "This is a stub file to provide an attachment point for " + mainNotesFilename);
			return AnnotationRepository.FromFile("id", fname, new NullProgress());
		}

		private void EnsureFileExists(string filename, string contentToCreateFileWith)
		{
			if (!File.Exists(filename))
			{
				using (var writer = new StreamWriter(filename, false, Encoding.UTF8))
				{
					writer.WriteLine(contentToCreateFileWith);
				}
			}
		}

		private Annotation CreateAnnotation(string content, string guidStr, string author, string status, string ownerGuidStr, string ownerShortName)
		{
			Guid guid;
			if (Guid.TryParse(guidStr, out guid))
			{
				if (guid == Guid.Empty)
				{
					guid = Guid.NewGuid();
				}
			}
			else
			{
				guid = Guid.NewGuid();
			}
			if (string.IsNullOrEmpty(author))
			{
				author = genericAuthorName;
			}
			var result = new Annotation("note", MakeFlexRefURL(ownerGuidStr, ownerShortName), guid, "ignored");
			result.AddMessage(author, LfStatusToChorusStatus(status), content);
			return result;
		}

		// TODO: Move this to a more appropriate class since the Get and WriteTo handlers both use it
		public static List<SerializableLfAnnotation> DecodeInputFile(string inputFilename)
		{
			var json = new DataContractJsonSerializer(typeof(List<SerializableLfAnnotation>));
			var utf8 = new UTF8Encoding(false);
			using (var stream = File.OpenRead(inputFilename))
			{
				return (List<SerializableLfAnnotation>)json.ReadObject(stream);
			}
		}

		private static string MakeFlexRefURL(string guidStr, string shortName)
		{
			return string.Format("silfw://localhost/link?app=flex&database=current&server=&tool=default&guid={0}&tag=&id={0}&label={1}", guidStr, shortName);
		}

		IEnumerable<Annotation> GetAllAnnotations(string projectDir)
		{
			var nullProgress = new NullProgress();
			return (
				from repo in AnnotationRepository.CreateRepositoriesFromFolder(projectDir, nullProgress)
				from ann in repo.GetAllAnnotations()
				select ann
			);
		}

		/// <summary>
		/// Get the type of action supported by the handler.
		/// </summary>
		ActionType IBridgeActionTypeHandler.SupportedActionType
		{
			get { return ActionType.LanguageForgeWriteToChorusNotes; }
		}

		#endregion IBridgeActionTypeHandler impl

		#region IDisposable impl

		/// <summary>
		/// Finalizer, in case client doesn't dispose it.
		/// Force Dispose(false) if not already called (i.e. m_isDisposed is true)
		/// </summary>
		~LanguageForgeWriteToChorusNotesActionHandler()
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