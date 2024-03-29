--GetStudentCoursesForEvaluation

Declare @RunAsOf date = '2008-10-01',--Null,
		@Debug bit = 0,
		@Development bit = 0;

Set @Development = 1;

Declare @People table(PersonID varchar(10));
Insert Into @People Values('0300996');

Use coll18_live;

If @RunAsOf Is Null Begin Set @RunAsOf = GetDate() End;

Begin -- Terms Table
	Declare @Terms table(
		POS int, IsCurrent bit, TermCode varchar(10), TermName varchar(100),
		TermBeginDate date, TermEndDate date, GradesDueDate date);
	Insert Into @Terms 
	Select		Row_Number() Over (Order By DATEDIFF(day, @RunAsOf, t.TERM_START_DATE)) As POS,
				Iif(t.TERM_START_DATE <= @RunAsOf And t.TERM_END_DATE >= @RunAsOf, 1, 0) As IsCurrent,
				t.TERMS_ID As TermCode, TERM_DESC As TermName,
				TERM_START_DATE As TermBeginDate, TERM_END_DATE As TermEndDate,
				IsNull(ts.TERMX_GRADES_DUE_DATE_S67, DateAdd(week, 1, TERM_END_DATE))
	From		TERMS t Inner Join TERMS_S67 ts On ts.TERMS_S67_ID = t.TERMS_ID
	Where		Len(t.TERMS_ID) = 3
	Order By	t.TERM_START_DATE;
	
	Declare @POS int = (Select POS From @Terms Where IsCurrent = 1);
	Delete From @Terms Where TermCode Not In (Select TermCode From @Terms t Where t.POS - @POS >= 0 And t.POS - @POS <= 1)
End;

Begin -- Sections
	Declare	@Sections table(SectionID varchar(20), TermCode varchar(10), CourseBeginDate date, CourseEndDate date);
	Insert Into @Sections
	Select		cs.COURSE_SECTIONS_ID, cs.SEC_TERM, SEC_START_DATE, SEC_END_DATE
	From		COURSE_SECTIONS cs
				Inner Join @Terms t
					On t.TermCode = cs.SEC_TERM
				Inner Join COURSE_SECTIONS_LS csls
					On cs.COURSE_SECTIONS_ID = csls.COURSE_SECTIONS_ID
					And csls.POS = Iif(cs.SEC_ACAD_LEVEL = 'GR', 2, 1)

	/* Development */
	Update		@Sections
	Set			CourseBeginDate = '2008-10-14', CourseEndDate = '2008-10-19'
	Where		SectionID = 19454
	/* Development */
End;

Select		PersonID As StudentID,
			IsNull(p.PREFERRED_NAME, IsNull(p.NICKNAME, p.FIRST_NAME) + ' ' + p.LAST_NAME) As StudentName,
			csf.COURSE_SEC_FACULTY_ID As EvaluationID,
			t.TermName,
			stac.STC_COURSE_NAME + ': ' + stac.STC_TITLE As CourseName,
			IsNull(f.PREFERRED_NAME, IsNull(f.NICKNAME, f.FIRST_NAME) + ' ' + f.LAST_NAME) As ProfessorName,
			t.TermBeginDate,
			t.TermEndDate,
			cs.CourseBeginDate,
			cs.CourseEndDate,
			t.GradesDueDate
			--'' As ' ', stac.STC_ACAD_LEVEL, cs.*
From		@People
			Inner Join PERSON p
				On p.ID = PersonID
			Inner Join STUDENT_ACAD_CRED stac
				On stac.STC_PERSON_ID = PersonID
			Inner Join @Terms t
				On t.TermCode = stac.STC_TERM
			Inner Join STUDENT_COURSE_SEC scs
				On scs.STUDENT_COURSE_SEC_ID = stac.STC_STUDENT_COURSE_SEC
			Inner Join @Sections cs
				On cs.SectionID= scs.SCS_COURSE_SECTION
			Inner Join COURSE_SEC_FACULTY csf
				On csf.CSF_COURSE_SECTION = scs.SCS_COURSE_SECTION
			Inner Join PERSON f
				On f.ID = csf.CSF_FACULTY
Order By	t.POS, stac.STC_TITLE;
	
If @Development = 1 Begin
	Select '@Variables' As 'DebugOutput', @RunAsOf As '@RunAsOf';
	Select '@People' As 'DebugOutput', * From @People;
	Select '@Terms' As 'DebugOutput', * From @Terms;
	Select '@Sections' As 'DebugOutput', * From @Sections;
End;

	/*
Exec usp_CE_GetStudentCoursesForEvaluation
	@PersonID = '0300996',
	@RunAsOf = '2008-10-01',
	@Debug = 1;
	
Exec usp_CE_GetStudentCoursesForEvaluation
	@PersonID = '0510454',--'0501072',
	@RunAsOf = '2023-12-01',
	@Debug = 0;
	
Exec usp_CE_GetStudentCoursesForEvaluation
	@PersonID = '0501072',
	@RunAsOf = '2023-12-01',
	@Debug = 0;
	*/