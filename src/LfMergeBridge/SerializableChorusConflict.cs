using System;
using System.Runtime.Serialization;

namespace FLEx_ChorusPlugin.Infrastructure.ActionHandlers
{
	[DataContract]
	public class SerializableChorusConflict
	{
		[DataMember] public string Description { get; set; }
		[DataMember] public Guid Guid { get; set; }
		[DataMember] public string HtmlDetails { get; set; }
		[DataMember] public bool IsNotification { get; set; }
		[DataMember] public string RelativeFilePath { get; set; }
		[DataMember] public string RevisionWhereMergeWasCheckedIn { get; set; }
		[DataMember] public string WinnerId { get; set; }

		// Fields from Situation property (of type MergeSituation)
		[DataMember] public string AlphaUserId { get; set; }
		[DataMember] public string BetaUserId { get; set; }
		[DataMember] public string AlphaUserRevision { get; set; }
		[DataMember] public string BetaUserRevision { get; set; }

		// Fields from Context property (of type ContextDescriptor)
		[DataMember] public string PathToUserUnderstandableElement { get; set; }
		[DataMember] public string DataLabel { get; set; }

		public SerializableChorusConflict() {}
		public SerializableChorusConflict(Chorus.merge.IConflict orig)
		{
			Description = orig.Description;
			Guid = orig.Guid;
			HtmlDetails = orig.HtmlDetails;
			IsNotification = orig.IsNotification;
			RelativeFilePath = orig.RelativeFilePath;
			RevisionWhereMergeWasCheckedIn = orig.RevisionWhereMergeWasCheckedIn;
			WinnerId = orig.WinnerId;

			AlphaUserId = orig.Situation.AlphaUserId;
			BetaUserId = orig.Situation.BetaUserId;
			AlphaUserRevision = orig.Situation.AlphaUserRevision;
			BetaUserRevision = orig.Situation.BetaUserRevision;

			PathToUserUnderstandableElement = orig.Context.PathToUserUnderstandableElement;
			DataLabel = orig.Context.DataLabel;
		}
	}
}