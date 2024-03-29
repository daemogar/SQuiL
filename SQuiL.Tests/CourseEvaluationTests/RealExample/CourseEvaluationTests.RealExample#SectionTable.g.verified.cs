﻿//HintName: SectionTable.g.cs
// <auto-generated />

#nullable enable

namespace CourseEvaluation.Application.Data;

public partial record SectionTable(
	string SectionID,
	string Department,
	string CourseCode,
	string CourseTitle,
	bool IsOnline,
	bool IsGraduateCourse,
	bool IsAdultDegreeCourse,
	bool IsNursingCourse,
	bool IsConnectionsCourse,
	bool IsPrivateMusicLessons,
	bool IsServiceLearning);
