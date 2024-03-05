
Declare	@Param_Debug bit = 1,
		@Param_SectionID varchar(20);

Set @SectionID = '3543'; -- Chemistry Course with Section Questions W02
Set @SectionID = '12748'; -- Engineering 121 Connections F05
Set @SectionID = '10675'; -- Music Lessons 334 F05

Declare @Returns_Sections table(
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

Use BIWarehouse;

Insert Into @Returns_Sections
Select		SectionID, DeptName, CourseName, CourseTitle, Iif(Upper(MeetingRm) = 'ONLINE', 1, 0), Iif(CourseLevel = 'GR', 1, 0), IsADC,
			Iif(CourseName Like 'NURG-%' Or CourseName Like 'NRNT-125-%', 1, 0),
			Iif(CourseName Like 'NOND-101-%' Or CourseName Like 'ENGR-121-%', 1, 0),
			Iif(CourseName Like 'MUPF-334-%', 1, 0), Case
				When GenEd In ('SERV1','SERV2','SERV3') Then 1
				When GenEd2 In ('SERV1','SERV2','SERV3') Then 1
				When GenEd3 In ('SERV1','SERV2','SERV3') Then 1
				When GenEd4 In ('SERV1','SERV2','SERV3') Then 1
				When GenEd5 In ('SERV1','SERV2','SERV3') Then 1
				Else 0
			End
From		adm.Sections s
Where		s.SectionID = @Param_SectionID;

Select * From @Returns_Sections;
