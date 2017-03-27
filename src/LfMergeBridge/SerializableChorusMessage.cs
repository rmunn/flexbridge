using System;
using System.Runtime.Serialization;

namespace FLEx_ChorusPlugin.Infrastructure.ActionHandlers
{
	[DataContract]
	public class SerializableChorusMessage
	{
		[DataMember] public string Author { get; set; }
		[DataMember] public DateTime Date { get; set; }
		[DataMember] public string Guid { get; set; }
		[DataMember] public string Status { get; set; }
		[DataMember] public string Text { get; set; }
		[DataMember] public string Xml { get; set; }  // From original message's Element property

		public SerializableChorusMessage() {}
		public SerializableChorusMessage(Chorus.notes.Message orig)
		{
			Author = orig.Author;
			Date = orig.Date;  // TODO: Must check how this gets serialized -- UTC? Does .Net assume that it's local?
			Guid = orig.Guid;
			Status = orig.Status;
			Text = orig.Text;

			Xml = orig.Element.ToString();
		}
	}
}