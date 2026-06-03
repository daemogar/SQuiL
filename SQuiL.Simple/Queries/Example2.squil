
Declare		@PersonID varchar(10),
			@Debug bit = 1;

Set	@PersonID = '0300996';

Declare	@Return_Participation table(
	SectionID varchar(20),
	PersonID varchar(10),
	ProfessorID varchar(10),
	TermCode varchar(10),
	CompletedDate datetime
);

Declare	@Return_Overrides table(
	SectionID varchar(20),
	TermCode varchar(10),
	CourseCode varchar(20),
	BeginDate datetime,
	EndDate datetime
);

Use DataRepositoryTest;

Insert Into @Return_Participation
Select * From (
Select		--pv.ElementId As ElementID,
			Max(Iif(pv.PropertyName = 'ParticipationSectionId', PropertyValue, Null)) as SectionID,
			Max(Iif(pv.PropertyName = 'ParticipationStudentId', PropertyValue, Null)) as PersonID,
			Max(Iif(pv.PropertyName = 'ParticipationTeacherId', PropertyValue, Null)) as ProfessorID,
			Max(Iif(pv.PropertyName = 'ParticipationTerm', PropertyValue, Null)) as TermCode,
			Max(Iif(pv.PropertyName = 'ParticipationCompletedOn', Cast(PropertyValue As datetime), Null)) as CompletedDate
From		CourseEvaluation_PropertyValues pv
			Inner Join (
				Select		ElementId
				From		CourseEvaluation_PropertyValues
				Where		PropertyName = 'Tag'
							And PropertyValue = 'Participation'
			) tags
				On tags.ElementId = pv.ElementId
Group By	pv.ElementId
) list
Where PersonID = @PersonID;

Insert Into @Return_Overrides
Select		Max(Iif(pv.PropertyName = 'SectionDateSectionId', PropertyValue, Null)) as SectionID,
			Max(Iif(pv.PropertyName = 'SectionDateTerm', PropertyValue, Null)) as TermCode,
			Max(Iif(pv.PropertyName = 'SectionDateDescription', PropertyValue, Null)) as CourseCode,
			Max(Iif(pv.PropertyName = 'SectionDateEvaluationBeginDate', Cast(PropertyValue As date), Null)) as BeginDate,
			Max(Iif(pv.PropertyName = 'SectionDateEvaluationEndDate', Cast(PropertyValue As date), Null)) as EndDate
From		CourseEvaluation_PropertyValues pv
			Inner Join (
				Select		ElementId
				From		CourseEvaluation_PropertyValues
				Where		PropertyName = 'Tag'
							And PropertyValue = 'SectionDate'
			) tags
				On tags.ElementId = pv.ElementId
Group By	pv.ElementId

Select * From @Return_Participation;
Select * From @Return_Overrides;

/*

ParticipationCompletedOn
ParticipationSectionId
ParticipationStudentId
ParticipationTeacherId
ParticipationTerm

*/