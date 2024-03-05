
Declare	@Param_Debug bit = 1,
		@Param_PersonID varchar(10),
		@Param_CourseCode varchar(20) = Null,
		@Param_AsOfDate date = Null;

Set @Param_PersonID = '0300996';
--Set @CourseCode = 'MATH-120-A';
Set @Param_AsOfDate = '2008-09-19';

Declare @Params_Terms table(TermCode varchar(10));
Insert Into @Params_Terms Select 'F08';
Insert Into @Params_Terms Select 'F06';

Declare	@Params_Participation table(
	SectionID varchar(20),
	PersonID varchar(10),
	ProfessorID varchar(10),
	TermCode varchar(10),
	CompletedDate datetime
);

Insert Into @Participation
Select '20655', @PersonID, '0300801', 'F08', '2023-11-29';

Declare	@Params_Overrides table(
	SectionID varchar(20),
	TermCode varchar(10),
	CourseCode varchar(20),
	BeginDate datetime,
	EndDate datetime
);

Insert Into @Overrides
Select '19012', 'F08', 'CPTR-365-A', '2023-09-15', '2023-11-20';

Declare @Returns_Courses table(
	EvalationID varchar(20),
	TermCode varchar(10),
	--SectionID varchar(20),
	CourseCode varchar(20),
	CourseTitle varchar(100),
	ProfessorPicture varchar(1000),
	ProfessorName varchar(100),
	EvaluationState varchar(6),
	EvaluationStatus varchar(50)
);

Use BIWarehouse;

If @Param_AsOfDate Is Null Set @Param_AsOfDate = GetDate();

Begin -- Courses
	Declare @Courses table(
		SectionID varchar(20),
		PersonID varchar(10),
		TermCode varchar(10),
		CourseCode varchar(20),
		CourseTitle varchar(100),
		BeginDate date,
		EndDate date)
	Insert Into @Courses
	Select		ss.SectionID, ss.PersonID, s.SectionTermActual, s.CourseName, s.CourseTitle,
				Case
					When o.BeginDate Is Not Null And o.BeginDate < t.StartDate Then t.StartDate 
					When o.BeginDate Is Not Null Then o.BeginDate
					When s.SectionStartDate < DateAdd(week, -4, s.SectionEndDate) Then DateAdd(week, -4, s.SectionEndDate)
					Else s.SectionStartDate
				End,
				Case 
					When o.EndDate Is Not Null And DateAdd(week, 1, o.EndDate) > t.GradesDueDate Then t.GradesDueDate
					When o.EndDate Is Not Null And DateAdd(week, 1, o.EndDate) > DateAdd(week, 1, t.EndDate) Then DateAdd(week, 1, t.EndDate)
					When o.EndDate Is Not Null Then DateAdd(week, 1, o.EndDate)
					When DateAdd(week, 1, s.SectionEndDate) > t.GradesDueDate Then t.GradesDueDate
					Else DateAdd(week, 1, s.SectionEndDate)
				End
	From		adm.StudentSections ss
				Inner Join adm.Sections s
					On s.SectionID = ss.SectionID
				Inner Join pub.Terms t
					On t.Term = s.SectionTermActual
				Inner Join @Terms tt
					On t.Term = tt.TermCode
				Left Join @Overrides o
					On o.SectionID = ss.SectionID
					And o.CourseCode = s.CourseName
					And o.TermCode = s.SectionTermActual
	Where		ss.PersonID = @PersonID
				And (@CourseCode Is Null Or s.CourseName = @CourseCode)
				And (
					GetDate() Between t.PreRegStartDate And DateAdd(week, 2, t.EndDate)
					Or s.SectionTermActual = t.Term
				);

	If @PersonID = '0300996' Begin
		Update		@Courses
		Set			BeginDate = '2008-10-15',
					EndDate = '2008-10-26'
		Where		SectionID = '19454'
	End;

	Update		@Courses
	Set			BeginDate = Case Format(BeginDate, 'dddd')
					When 'Friday' Then DateAdd(day, -1, BeginDate)
					When 'Saturday' Then DateAdd(day, 1, BeginDate)
					Else BeginDate
				End,
				EndDate = Case Format(EndDate, 'dddd')
					When 'Friday' Then DateAdd(day, -1, EndDate)
					When 'Saturday' Then DateAdd(day, 1, EndDate)
					Else EndDate
				End;

End;

Insert Into @Returns_Courses
Select EvaluationID, TermCode, CourseCode, CourseTitle, PictureLink, PreferredName, Trim(SubString(EvaluationStatus, 1, 6)), SubString(EvaluationStatus, 8, 1000) From (
Select		Char(64 + sf.FacultyOrder) + Cast(sf.SectionFacultyID As varchar(10)) As EvaluationID,
			c.TermCode, /*c.SectionID,*/ c.CourseCode, c.CourseTitle, p.PictureLink, p.PreferredName, Case
				When e.CompletedDate Is Not Null Then 'DONE  :Completed On ' + Format(e.CompletedDate, 'dddd, MMMM d')
				When @Param_AsOfDate < c.BeginDate Then 'OPENS :Opens On ' + Format(c.BeginDate, 'dddd, MMMM d')
				When @Param_AsOfDate < c.EndDate Then 'OPEN  :Open Until ' + Format(c.EndDate, 'dddd, MMMM d')
				Else 'CLOSED:Closed On ' + Format(c.EndDate, 'dddd, MMMM d')
			End EvaluationStatus
From		@Courses c
			Inner Join adm.SectionFaculty sf
				On sf.SectionID = c.SectionID
			Inner Join pub.spPerson p
				On p.PersonID = sf.PersonID
			Left Join @Participation e
				On e.SectionID = c.SectionID
				And e.PersonID = c.PersonID
				And e.ProfessorID = sf.PersonID
				And e.TermCode = c.TermCode
) list;

Select * From @Returns_Courses;

If @Param_Debug = 1 Begin
	Select '@Variables' As [TableName], @Lookup As '@Lookup';
	Select '@Courses' As [TableName], * From @Courses;
	Select '@Participation' As [TableName], * From @Returns_Participation;
	Select '@Overrides' As [TableName], * From @Returns_Overrides;
End;