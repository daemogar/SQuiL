Create Temp Table Returns_Student (StudentID INTEGER not null Primary Key, Name TEXT not null);
Create Temp Table Returns_Course (CourseID INTEGER not null Primary Key, Title TEXT not null);
Create Temp Table Returns_Enrollment (StudentID INTEGER not null, CourseID INTEGER not null, Grade TEXT not null);
Insert Into Returns_Student (StudentID, Name) Values (1, 'Ada'), (2, 'Alan');
Insert Into Returns_Course (CourseID, Title) Values (10, 'Math'), (11, 'Art');
Insert Into Returns_Enrollment (StudentID, CourseID, Grade) Values (1, 10, 'A'), (1, 11, 'B'), (2, 10, 'C');
Select StudentID, Name From Returns_Student;
Select CourseID, Title From Returns_Course;
Select StudentID, CourseID, Grade From Returns_Enrollment;
