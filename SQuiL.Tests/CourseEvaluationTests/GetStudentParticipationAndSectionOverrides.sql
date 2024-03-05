
Declare		@Param_Debug bit = 1,
			@Param_PersonID varchar(10);

Set	@Param_PersonID = '0300996';

Declare @Params_Terms table(TermCode varchar(10));
Insert Into @Params_Terms Select 'F08';
Insert Into @Params_Terms Select 'F06';

Declare	@Returns_Participation table(
	SectionID varchar(20),
	PersonID varchar(10),
	ProfessorID varchar(10),
	TermCode varchar(10),
	CompletedDate datetime
);

Declare	@Returns_Overrides table(
	SectionID varchar(20),
	TermCode varchar(10),
	CourseCode varchar(20),
	BeginDate datetime,
	EndDate datetime
);

Use DataRepository;--Test;

Insert Into @Returns_Participation
Select list.* From (
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
) list Inner Join @Params_Terms t On list.TermCode = t.TermCode
Where		PersonID = @PersonID;

Insert Into @Returns_Overrides
Select list.* From (
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
) list Inner Join @Params_Terms t On list.TermCode = t.TermCode;

Select * From @Returns_Participation;
Select * From @Returns_Overrides;

Select * From @Params_Terms;

/*

ParticipationCompletedOn
ParticipationSectionId
ParticipationStudentId
ParticipationTeacherId
ParticipationTerm

*/