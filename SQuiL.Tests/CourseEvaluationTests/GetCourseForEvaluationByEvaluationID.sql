
Declare		@Param_EvaluationID varchar(21);
Set @Param_EvaluationID = 'A25311';

Declare		@Return_SectionID varchar(10) = '';
Declare		@Return_PersonID varchar(10) = '';
Declare		@Return_TermCode varchar(10) = '';

Use BIWarehouse;

Select		@Return_SectionID = sf.SectionID,
			@Return_PersonID = sf.PersonID,
			@Return_TermCode = s.SectionTermActual
From		adm.SectionFaculty sf
			Inner Join adm.Sections s
				On s.SectionID = sf.SectionID
			Inner Join pub.spPerson p
				On p.PersonID = sf.PersonID
Where		Char(64 + sf.FacultyOrder) + Cast(sf.SectionFacultyID As varchar(10)) = @Param_EvaluationID;

Select @Return_SectionID;
Select @Return_PersonID;
Select @Return_TermCode;
