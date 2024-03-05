
Declare	@Param_Debug bit = 1;

Declare @Param_Section table(
	SectionID varchar(20),
	Department varchar(100),
	CourseCode varchar(20),
	CourseTitle varchar(150),
	IsOnline bit,
	IsGraduateCourse bit,
	IsAdultDegreeCourse bit,
	IsNursingCourse bit,
	IsConnectionsCourse bit,
	IsPrivateMusicLessons bit,
	IsServiceLearning bit
);

Declare @Returns_Questions table(
	SectionID varchar(20),
	Category varchar(200),
	[Type] varchar(20),
	Question varchar(1000)
);

Use DataRepository;

Insert Into @Returns_Questions
Select		s.SectionID,
			Case ShowWhen
				When 'global' Then 'Global Questions'
				When 'online' Then 'Online Questions'
				When 'graduate' Then 'Graduate Questions'
				When 'department' Then s.Department + ' Questions'
				When 'section' Then s.CourseTitle + ' Questions (' + s.CourseCode + ')'
				When 'service_learning' Then 'Service Learning Questions'
				When 'nursing' Then 'Nursing Questions'
				When 'connections' Then 'Southern Connections Questions'
				When 'private_music_instruction' Then 'Private Music Leason Questions'
				Else Null
			End,
			Upper(QuestionType) As QuestionType,
			QuestionText From (
Select		--pv.ElementId As ElementID,
			Cast(Max(Iif(pv.PropertyName = 'QuestionEnabled', Iif(PropertyValue = 'True', 1, 0), 0)) As bit) As IsEnabled,
			Cast(Max(Iif(pv.PropertyName = 'QuestionOrder', PropertyValue, Null)) As int) As SortOrder,
			--Max(Iif(pv.PropertyName = 'QuestionSectionId', PropertyValue, Null)) As SectionID,
			Max(Iif(pv.PropertyName = 'QuestionShowCondition', PropertyValue, Null)) As Condition,
			Max(Iif(pv.PropertyName = 'QuestionShowWhen', Iif(PropertyValue = 'division', 'department', PropertyValue), Null)) As ShowWhen,
			Max(Iif(pv.PropertyName = 'QuestionText', PropertyValue, Null)) As QuestionText,
			Max(Iif(pv.PropertyName = 'QuestionType', Case PropertyValue
				When 'likert' Then PropertyValue
				When 'shortanswer' Then PropertyValue
				Else Null
			End, Null)) As QuestionType
			--Max(Iif(pv.PropertyName = 'TermCode', SubString(PropertyValue, 1, 3), Null)) As TermCode
From		CourseEvaluation_PropertyValues pv
			Inner Join (
				Select		ElementId
				From		CourseEvaluation_PropertyValues
				Where		PropertyName = 'Tag'
							And PropertyValue = 'EvaluationQuestion'
			) tags
				On tags.ElementId = pv.ElementId
Group By	pv.ElementId
) list Cross Join @Sections s
Where		IsEnabled = 1 And Case ShowWhen
				When 'global' Then 1
				When 'online' Then IsOnline
				When 'graduate' Then IsGraduateCourse
				When 'department' Then Iif(Department = Condition, 1, 0)
				When 'section' Then Iif(list.Condition = s.SectionID, 1, 0)
				When 'service_learning' Then IsServiceLearning
				When 'nursing' Then IsNursingCourse
				When 'connections' Then IsConnectionsCourse
				When 'private_music_instruction' Then IsPrivateMusicLessons
				Else 0
			End = 1
Order By	list.QuestionType, Case ShowWhen
				When 'global' Then 1
				When 'online' Then 2
				When 'graduate' Then 3
				When 'department' Then 4
				When 'section' Then 5
				When 'service_learning' Then 6
				When 'nursing' Then 7
				When 'connections' Then 8
				When 'private_music_instruction' Then 9
				Else 99
			End, SortOrder

Select * From @Returns_Questions;

/*


global;
online=MATCH(section.MeetingRm,ONLINE);
graduate=MATCH(section.IsGraduateCourse,True);
department=section.DeptName;
section=sectionId;
service_learning=MATCH(section.GenEd,SERV1,SERV2,SERV3);
nursing=MATCH_BEGIN(section.CourseName,NRSG,NRNT-125);
connections=MATCH_BEGIN(section.CourseName,NOND-101,ENGR-121);
private_music_instruction=MATCH_BEGIN(section.CourseName,MUPF-334)



QuestionEnabled
QuestionOrder
QuestionSectionId
QuestionShowCondition
QuestionShowWhen
QuestionText
QuestionType
Tag
TermCode

*/


