using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace FLEx_ChorusPlugin.Infrastructure.ActionHandlers
{
	[DataContract]
	public class SerializableLfCommentReply
	{
		[DataMember] public string Guid { get; set; }
		[DataMember] public string AuthorNameAlternate { get; set; }
		[DataMember] public string Text { get; set; }
		[DataMember] public bool IsDeleted { get; set; }
		[DataMember] public string UniqId { get; set; }  // Called "id" in Mongo, but called "UniqId" in LfMerge
	}
}
