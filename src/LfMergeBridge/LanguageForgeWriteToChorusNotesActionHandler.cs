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

			// We need to serialize the Mongo ObjectIds of the SerializableLfAnnotation objects coming from LfMerge (they're called LfComment over there),
			// but we can't put them in the SerializableLfAnnotation definition
			string inputFilename = options[LfMergeBridge.LfMergeBridgeUtilities.serializedCommentsFromLfMerge];
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, $"Input filename: {inputFilename}");
			string data = File.ReadAllText(inputFilename);
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, $"Input data: {data}");

			List<KeyValuePair<string, SerializableLfComment>> commentsFromLF = LfMergeBridge.LfMergeBridgeUtilities.DecodeJsonFile<List<KeyValuePair<string, SerializableLfComment>>>(inputFilename);
			AnnotationRepository[] annRepos = GetAnnotationRepositories(progress);
			AnnotationRepository primaryRepo = annRepos[0];

			// The LINQ-based approach in the following line does NOT work, because there can be duplicate keys for some reason.
			// Dictionary<string, Annotation> chorusAnnotationsByGuid = annRepos.SelectMany(repo => repo.GetAllAnnotations()).ToDictionary(ann => ann.Guid, ann => ann);
			// Instead we have to do it by hand:
			var chorusAnnotationsByGuid = new Dictionary<string, Annotation>();
			foreach (Annotation ann in annRepos.SelectMany(repo => repo.GetAllAnnotations()))
			{
				chorusAnnotationsByGuid[ann.Guid] = ann;
			}

			// Next step: go through all the annotations in the project dir and keep track of their GUIDs.
			// Then decide whether we need to add anything to them because the Messages have changed.
			// First criterion: how many messages are there?
			// Second criterion: go through either *content*, or *date modified*. (Decide which one). Probably date modified. (or AuthorInfo.DateModified)

			var commentIdsThatNeedGuids = new Dictionary<string,string>();
			var replyIdsThatNeedGuids = new Dictionary<string,string>();

			foreach (KeyValuePair<string, SerializableLfComment> kvp in commentsFromLF)
			{
				string lfAnnotationObjectId = kvp.Key;
				SerializableLfComment lfAnnotation = kvp.Value;
				if (lfAnnotation == null || lfAnnotation.IsDeleted)
				{
					if (lfAnnotation == null)
					{
						LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("Skipping null annotation with MongoId {0}",
							lfAnnotationObjectId ?? "(null ObjectId)"));
					}
					else
					{
						LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("Skipping deleted annotation {0} containing content \"{1}\"",
							lfAnnotation?.Guid ?? "(no guid)", lfAnnotation?.Content ?? "(no content)"));
					}
					continue;
				}
				// string ownerGuid = lfAnnotation.Regarding?.TargetGuid ?? string.Empty;
				// string ownerShortName = lfAnnotation.Regarding?.Word ?? "???";  // Match FLEx's behavior when short name can't be determined
				string ownerGuid = (lfAnnotation.Regarding == null) ? string.Empty : lfAnnotation.Regarding.TargetGuid;
				string ownerShortName = (lfAnnotation.Regarding == null) ? "???" : lfAnnotation.Regarding.Word;  // Match FLEx's behavior when short name can't be determined

				Annotation chorusAnnotation;
				if (lfAnnotation.Guid != null && chorusAnnotationsByGuid.TryGetValue(lfAnnotation.Guid, out chorusAnnotation) && chorusAnnotation != null)
				{
					SetChorusAnnotationMessagesFromLfReplies(chorusAnnotation, lfAnnotation, lfAnnotationObjectId, replyIdsThatNeedGuids, commentIdsThatNeedGuids);
					LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("Wrote annotation {0} containing messages [{1}]",
						chorusAnnotation.Guid, String.Join(", ", chorusAnnotation.Messages.Select(msg => "\"" + msg.Text + "\""))));
					if (lfAnnotation.Replies == null || lfAnnotation.Replies.Count == 0)
						LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, "... and it had no replies.");
				}
				else
				{
					Annotation newAnnotation = CreateAnnotation(lfAnnotation.Content, lfAnnotation.Guid, lfAnnotation.AuthorNameAlternate, lfAnnotation.Status, ownerGuid, ownerShortName);
					SetChorusAnnotationMessagesFromLfReplies(newAnnotation, lfAnnotation, lfAnnotationObjectId, replyIdsThatNeedGuids, commentIdsThatNeedGuids);
					LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("*NEW* annotation {0} with ref URL {2} containing messages [{1}]",
						newAnnotation.Guid, String.Join(", ", newAnnotation.Messages.Select(msg => "\"" + msg.Text + "\"")), newAnnotation.RefStillEscaped));
					if (lfAnnotation.Replies == null || lfAnnotation.Replies.Count == 0)
						LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, "... which had no replies.");
					primaryRepo.AddAnnotation(newAnnotation);
				}
			}

			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("New comment ID->Guid mappings: {0}",
				String.Join(";", commentIdsThatNeedGuids.Select(kv => String.Format("{0}={1}", kv.Key, kv.Value)))));
			LfMergeBridge.LfMergeBridgeUtilities.AppendLineToSomethingForClient(ref somethingForClient, String.Format("New reply ID->Guid mappings: {0}",
				String.Join(";", replyIdsThatNeedGuids.Select(kv => String.Format("{0}={1}", kv.Key, kv.Value)))));

			SaveReposIfNeeded(annRepos, progress);
		}

		private void SaveReposIfNeeded(IEnumerable<AnnotationRepository> repos, IProgress progress)
		{
			foreach (var repo in repos)
			{
				repo.SaveNowIfNeeded(progress);
			}
		}

		private string LfStatusToChorusStatus(string lfStatus)
		{
			if (lfStatus == SerializableLfComment.Resolved)
			{
				return Chorus.notes.Annotation.Closed;
			}
			else
			{
				return Chorus.notes.Annotation.Open;
			}
		}

		private void SetChorusAnnotationMessagesFromLfReplies(Annotation chorusAnnotation, SerializableLfComment annotationInfo, string annotationObjectId, Dictionary<string,string> uniqIdsThatNeedGuids, Dictionary<string,string> commentIdsThatNeedGuids)
		{
			// TODO: We'll need another parameter, or else a private instance variable, to build up a list of (PHP id, GUID) pairs
			// for communicating back to LfMerge at the end. I'd prefer another parameter, as an instance variable just hides the
			// dependency and makes it harder to test.

			// Any LF comments that do NOT yet have GUIDs need them set from the corresponding Chorus annotation
			if (String.IsNullOrEmpty(annotationInfo.Guid) && !String.IsNullOrEmpty(annotationObjectId))
			{
				commentIdsThatNeedGuids[annotationObjectId] = chorusAnnotation.Guid;
			}

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
				Message newChorusMsg = chorusAnnotation.AddMessage(reply.AuthorNameAlternate, statusToSet, reply.Content);
				if ((string.IsNullOrEmpty(reply.Guid) || reply.Guid == zeroGuidStr) && ! string.IsNullOrEmpty(reply.UniqId))
				{
					uniqIdsThatNeedGuids[reply.UniqId] = newChorusMsg.Guid;
					// uniqIdsThatNeedGuids[reply.UniqId] = Guid.NewGuid().ToString(); // Just for testing purposes. TODO: Remove this line entirely.
				}
			}
			// Since LF allows changing a comment's status without adding any replies, it's possible we haven't updated the Chorus status yet at this point.
			// But first, check for a special case. Oten, the Chorus annotation's status will be blank, which corresponds to "open" in LfMerge. We don't want
			// to add a blank message just to change the Chorus status from "" (empty string) to "open", so we need to detect this situation specially.
			if (String.IsNullOrEmpty(chorusAnnotation.Status) && statusToSet == Chorus.notes.Annotation.Open)
			{
				// No need for new status here
			}
			else if (statusToSet != chorusAnnotation.Status)
			{
				// LF doesn't keep track of who clicked on the "Resolved" or "Todo" buttons, so we have to be vague about authorship
				chorusAnnotation.SetStatus(genericAuthorName, statusToSet);
			}
		}

		private AnnotationRepository[] GetAnnotationRepositories(IProgress progress)
		{
			AnnotationRepository[] projectRepos = AnnotationRepository.CreateRepositoriesFromFolder(ProjectDir, progress).ToArray();
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

		private static string MakeFlexRefURL(string guidStr, string shortName)
		{
			return string.Format("silfw://localhost/link?app=flex&database=current&server=&tool=default&guid={0}&tag=&id={0}&label={1}", guidStr, shortName);
		}

		/// <summary>
		/// Get the type of action supported by the handler.
		/// </summary>
		ActionType IBridgeActionTypeHandler.SupportedActionType
		{
			get { return ActionType.LanguageForgeWriteToChorusNotes; }
		}

		#endregion IBridgeActionTypeHandler impl
	}
}
