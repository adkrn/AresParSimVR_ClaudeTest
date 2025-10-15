using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Text.RegularExpressions;


public static class CsvParser
{
    static string SPLIT_RE = @",(?=(?:[^""]*""[^""]*"")*(?![^""]*""))";
    static string LINE_SPLIT_RE = @"\r\n|\n\r|\n|\r";

    static char[] TRIM_CHARS = { '\"' };

    /// <summary>
    /// Csv 파일 불러오기
    /// </summary>
    /// <param name="file">불러올 파일명</param>
    /// <param name="header"></param>
    /// <returns></returns>
    public static List<Dictionary<string, object>> Read(string file)
    {
        var list = new List<Dictionary<string, object>>();
        string[] lines;
        //Debug.Log("Path: "+file);
        if (File.Exists(file))
        {
            string source;
            StreamReader sr = new StreamReader(file);
            source = sr.ReadToEnd();
            sr.Close();

            lines = Regex.Split(source, LINE_SPLIT_RE).Select(field => field.Trim('\"')).ToArray();
        }
        else
        {
            return null;
        }

        if (lines.Length <= 1) return list;

        var header = Regex.Split(lines[0], SPLIT_RE);
        for (var i = 1; i < lines.Length; i++)
        {

            var values = Regex.Split(lines[i], SPLIT_RE);
            if (values.Length == 0 || values[0] == "") continue;

            var entry = new Dictionary<string, object>();
            for (var j = 0; j < header.Length && j < values.Length; j++)
            {
                string value = values[j];
                value = value.TrimStart(TRIM_CHARS).TrimEnd(TRIM_CHARS).Replace("\\", "");
                value = value.Replace("<br>", "\n");
                value = value.Replace("<c>", ",");
                object finalvalue = value;
                entry[header[j]] = finalvalue;
            }

            list.Add(entry);
        }

        return list;
    }
    
    /// <summary>
    /// 읽어온 csv 데이터에서 헤더 정보만 가져온다.
    /// </summary>
    /// <param name="file"></param>
    /// <param name="whichPath"></param>
    /// <returns></returns>
    public static string[] GetHeader(string file)
    {
        var list = new List<Dictionary<string, object>>();

        TextAsset data = new TextAsset();
        var lines = new string[] { };

                data = Resources.Load(file) as TextAsset;
                lines = Regex.Split(data.text, LINE_SPLIT_RE);


        var header = Regex.Split(lines[0], SPLIT_RE);
        return header;
    }
    
    
    public static List<Dictionary<string, object>> ReadWithHeader(DataName name, out string[] header)
    {
        var list = new List<Dictionary<string, object>>();
        string[] rawLines;
        // Debug.Log("Path: " + filePath);

        // 파일 경로가 존재하지 않을 경우 오류 메시지 출력
        var filePath = FileUtils.GetFilePath(FilePath.CSV) + name.ToString() + ".csv";
        if (!File.Exists(filePath))
        {
            Debug.LogError("CSV file not found at path: " + filePath);
            header = new string[] { };
            return null;
        }

        /////////////////////////////////////////
        // 파일이 존재할 경우 아래 내용을 실행합니다. ///
        /////////////////////////////////////////

        // 파일의 모든 라인을 읽어서 배열에 저장합니다.

        Debug.Log($"File Path: {filePath}");
        
        StreamReader sr = new StreamReader(filePath);
        string source = sr.ReadToEnd();
        sr.Close();

        rawLines = Regex.Split(source, LINE_SPLIT_RE);

        Debug.Log("Load: " + filePath);

        // 헤더 데이터 로드
        header = Regex.Split(rawLines[0], SPLIT_RE);
        string currentLine = "";

        // 헤더밖에 없으면 빈 리스트 반환
        if (rawLines.Length <= 1) return list;

        // 데이터를 파싱하여 Dictionary에 저장합니다.
        for (int i = 1; i < rawLines.Length; i++)
        {
            // 한 줄의 데이터 크기가 헤더와 크기가 같은지 비교하여 처리하는 비교구문
            // 동일하면 currentLine이 초기화 되어 루프 초기로 온다.
            if (string.IsNullOrEmpty(currentLine)) currentLine = rawLines[i];
            // 동일하지 않으면 currentLine이 초기화 되지 않고 루프를 돌면서 데이터를 추가한다.
            else currentLine += "\n" + rawLines[i];

            // 헤더의 길이와 일치하는 경우에만 데이터를 처리합니다.
            // string[] fields = Regex.Split(currentLine, LINE_SPLIT_RE).Select(field => field.Trim('\"')).ToArray();
            // string[] fields = Regex.Split(currentLine, @",(?=(?:[^,]*,[^,]*)*(?![^,]*,))");
            string[] fields = Regex.Split(currentLine, @",(?=(?:[^""]*""[^""]*"")*(?![^""]*""))");
            // string[] fields = Regex.Split(currentLine, @",(?=(?:[^""]*""[^""]*"")*(?![^""]*""))");
            
            for (int k = 0; k < fields.Length; k++)
            {
                fields[k] = fields[k].Trim('"');
            }

            if (fields.Length >= header.Length)
            {
                // CSV 셀 내부에 포함된 줄 바꿈 문자를 공백으로 대체하여 줄 바꿈 문제 해결
                for (int j = 0; j < fields.Length; j++)
                {
                    fields[j] = fields[j].Replace("\n", " ").Replace("\r", " ");
                }

                // 헤더와 각 필드 값을 매핑하여 Dictionary에 저장합니다.
                Dictionary<string, object> entry = new Dictionary<string, object>();
                for (int j = 0; j < header.Length && j < fields.Length; j++)
                {
                    entry[header[j]] = fields[j];
                }

                // 파싱된 데이터를 리스트에 추가합니다.
                list.Add(entry);
                currentLine = ""; // 현재 줄 초기화
            }
        }

        // 데이터 저장이 필요할 경우 아래 메서드를 활성화 후 경로 및 파일명 등 변경이 필요한 부분을 수정하여 사용할 것
        // SaveCleanedCSV(filePath, list);
        return list;
    }
    
     /// <summary>
    /// Data에 수정된 정보를 업데이트
    /// 두개의 변수가 이름이 같아야 된다.
    /// </summary>
    /// <param name="dataDic">Csv Data</param>
    /// <param name="header">Csv Header</param>>
    public static List<T> UpdateData<T>(List<Dictionary<string, object>> dataDic, string[] header) where T : new()
    {
        List<T> dataList = new List<T>();
        foreach (var data in dataDic)
        {
            T updateData = new T();

            foreach (var h in header)
            {
                // csv header 값과 일치하는 필드 찾기
                var field = updateData.GetType().GetField(h);
                if (field == null) continue;

                var fieldType = field.FieldType;

                // Enum 타입인 경우
                if (fieldType.IsEnum)
                {
                    // CSV 원본 값
                    string raw = data[h]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(raw)) continue;   // 빈 칸이면 스킵

                    // 대소문자 무시 & 안전 파싱
                    if (Enum.TryParse(fieldType, raw, ignoreCase: true, out object parsed))
                    {
                        field.SetValue(updateData, parsed);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[CsvParser] Enum 파싱 실패 → Field:{h}, Value:'{raw}', EnumType:{fieldType.Name}");
                    }
                    continue;   // 열거형 처리 후 다음 헤더로
                }
                else
                {
                    // 열거형이 아닌 다른 타입들 처리
                    var typeName = fieldType.ToString().Split('.').Last();

                    switch (typeName)
                    {
                        case "String":
                            field.SetValue(updateData, data[h].ToString());

                            break;
                        case "Int32":
                            field.SetValue(updateData, int.Parse(data[h].ToString()));
                            break;
                        case "Boolean":
                            field.SetValue(updateData, Convert.ToBoolean(data[h].ToString()));
                            break;
                        case "Single":
                            field.SetValue(updateData, float.Parse(data[h].ToString()));
                            break;
                    }
                }
            }

            // 이후 데이터 통합 작업 후 아래 메소드를 호출하여 사용하도록 수정
            dataList.Add(updateData);
        }

        return dataList;
    }
}