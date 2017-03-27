using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace FLEx_ChorusPlugin.Infrastructure.ActionHandlers
{
	[DataContract]
	public class SerializableChorusAnnotation
	{
		public static readonly string Open = Chorus.notes.Annotation.Open;
		public static readonly string Closed = Chorus.notes.Annotation.Closed;
		public static string TimeFormatNoTimeZone = Chorus.notes.Annotation.TimeFormatNoTimeZone;

		[DataMember] public string AnnotationFilePath { get; set; }
		[DataMember] public bool CanResolve { get; set; }
		[DataMember] public string ClassName { get; set; }
		[DataMember] public DateTime Date { get; set; }
		[DataMember] public string Guid { get; set; }
		[DataMember] public string IconClassName { get; set; }
		[DataMember] public bool IsClosed { get; set; }
		[DataMember] public bool IsConflict { get; set; }
		[DataMember] public bool IsCriticalConflict { get; set; }
		[DataMember] public bool IsNotification { get; set; }
		[DataMember] public string LabelOfThingAnnotated { get; set; }
		[DataMember] public IEnumerable<SerializableChorusMessage> Messages { get; set; }
		[DataMember] public string RefStillEscaped { get; set; }
		[DataMember] public string RefUnEscaped { get; set; }
		[DataMember] public string Status { get; set; }
		[DataMember] public string Xml { get; set; }  // From original annotation's Element property

		public SerializableChorusAnnotation() {}
		public SerializableChorusAnnotation(Chorus.notes.Annotation orig)
		{
			AnnotationFilePath = orig.AnnotationFilePath;
			CanResolve = orig.CanResolve;
			ClassName = orig.ClassName;
			Date = orig.Date;  // TODO: Must check how this gets serialized -- UTC? Does .Net assume that it's local?
			Guid = orig.Guid;
			IconClassName = orig.IconClassName;
			IsClosed = orig.IsClosed;
			IsConflict = orig.IsConflict;
			IsCriticalConflict = orig.IsCriticalConflict;
			IsNotification = orig.IsNotification;
			LabelOfThingAnnotated = orig.LabelOfThingAnnotated;
			Messages = from msg in orig.Messages select new SerializableChorusMessage(msg);
			RefStillEscaped = orig.RefStillEscaped;
			RefUnEscaped = orig.RefUnEscaped;
			Status = orig.Status;

			Xml = orig.Element.ToString();
		}
	}
}